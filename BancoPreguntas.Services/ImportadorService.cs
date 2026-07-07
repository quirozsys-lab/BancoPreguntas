using BancoPreguntas.Core.Models;
using BancoPreguntas.Data.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace BancoPreguntas.Services;

public class ImportadorService
{
    private readonly AdminRepository _adminRepo;

    public ImportadorService(AdminRepository adminRepo)
    {
        _adminRepo = adminRepo;
    }

    // 1. EXTRAER METADATOS (Para mostrar en la pantalla antes de importar)
    public async Task<CuadernilloMetadatosDto?> ExtraerMetadatosPreliminaresAsync(MemoryStream stream, string nombreArchivo)
    {
        try
        {
            stream.Position = 0;
            using var doc = PdfDocument.Open(stream);
            var textoPortada = string.Join(" ", doc.GetPages().Take(2).Select(p => p.Text));

            var dto = new CuadernilloMetadatosDto { Codigo = Path.GetFileNameWithoutExtension(nombreArchivo) };

            var matchArea = Regex.Match(textoPortada, @"(A\d{2}-EBRS?-\d{2})\s*/\s*(.+?)(?=\s*\d\s*A\d{2}|INSTRUCCIONES|Concurso|Convocatoria)");
            if (matchArea.Success)
            {
                dto.Codigo = matchArea.Groups[1].Value.Trim();
                dto.Area = matchArea.Groups[2].Value.Trim();
            }
            var matchAnio = Regex.Match(textoPortada, @"Convocatoria\s+(\d{4})");
            if (matchAnio.Success)
                dto.Convocatoria = matchAnio.Groups[1].Value.Trim();
            return dto;
        }
        catch { return null; }
    }

    // 2. IMPORTACIÓN COMPLETA (El flujo que guarda en BD)
    // ============================================================
    // Reemplaza tu método ImportarCuadernilloCompletoAsync por este.
    // ============================================================

    public async Task<ResultadoImportacionDto> ImportarCuadernilloCompletoAsync(
        MemoryStream ms,
        string codigo,
        string area,
        string proposito,
        string convocatoria)
    {
        try
        {
            // Parsear preguntas y casos
            var (_, preguntas, casos) = ParsearPdfCuadernillo(ms, codigo);

            var cuad = new Cuadernillo
            {
                Codigo = codigo,
                Area = area,
                Proposito = proposito,
                Convocatoria = convocatoria,
                TotalPreguntas = preguntas.Count
            };

            // 1) Insertar Cuadernillo
            int cuadId = await _adminRepo.InsertarCuadernilloAsync(cuad);

            // 2) Insertar Casos primero, guardando el Id real que devuelve la BD
            //    Usamos un diccionario: indice temporal (posicion en 'casos') -> Id real en BD
            var mapaCasoIdReal = new Dictionary<int, int>();

            for (int i = 0; i < casos.Count; i++)
            {
                var caso = casos[i];
                var casoDb = new Caso
                {
                    CuadernilloId = cuadId,
                    Texto = caso.Texto,
                    PaginaInicioPdf = caso.PaginaInicioPdf ?? 0
                };

                int casoId = await _adminRepo.InsertarCasoAsync(casoDb);
                mapaCasoIdReal[i] = casoId;
            }
            foreach (var p in preguntas)
            {
                try
                {
                    int? casoIdReal = null;
                    if (p.CasoIndexTemporal.HasValue && mapaCasoIdReal.ContainsKey(p.CasoIndexTemporal.Value))
                        casoIdReal = mapaCasoIdReal[p.CasoIndexTemporal.Value];

                    var pregDb = new Pregunta
                    {
                        CuadernilloId = cuadId,
                        Numero = p.Numero,
                        Texto = p.Texto,
                        PaginaPdf = p.PaginaPdf ?? 0,
                        CasoId = casoIdReal,
                        NecesitaRevision = p.NecesitaRevision,
                        NotaRevision = p.NotaRevision,
                        ImagenPath = null
                    };

                    int pregId = await _adminRepo.InsertarPreguntaAsync(pregDb);

                    foreach (var alt in p.Alternativas)
                    {
                        try
                        {
                            alt.PreguntaId = pregId;
                            alt.ImagenPath ??= null;
                            await _adminRepo.InsertarAlternativaAsync(alt);
                        }
                        catch (Exception exAlt)
                        {
                            System.Diagnostics.Debug.WriteLine($"ERROR alt P{p.Numero} Letra={alt.Letra}: {exAlt.Message}");
                        }
                    }
                }
                catch (Exception exPrg)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR pregunta P{p.Numero}: {exPrg.Message}");
                }
            }
                       
            return new ResultadoImportacionDto
            {
                Exitoso = true,
                Mensaje = $"Importado con éxito: {preguntas.Count} preguntas, {casos.Count} casos.",
                CuadernilloId = cuadId
            };
        }
        catch (Exception ex)
        {
            return new ResultadoImportacionDto { Exitoso = false, Mensaje = ex.Message };
        }
    }
    // ============================================================
    // PARSER FINAL v4
    // Estrategia:
    // 1. Detectar marcadores de pregunta por coordenadas (X < 75, no footer)
    // 2. Para cada pregunta, extraer palabras del bloque por rango de Y
    // 3. Reconstruir texto agrupando palabras por línea (tolerancia 5pts)
    // 4. Aplicar regex de alternativas sobre texto reconstruido
    // ============================================================

    private (Cuadernillo cuad, List<PreguntaTemp> preguntas, List<CasoTemp> casos)
        ParsearPdfCuadernillo(MemoryStream stream, string codigo)
    {
        stream.Position = 0;
        using var doc = PdfDocument.Open(stream);

        var preguntas = new List<PreguntaTemp>();
        var casos = new List<CasoTemp>();
        var preguntaACaso = new Dictionary<int, int>();

        const double xMargenNumero = 75;
        const double topFooterMax = 55;      // Y-up: footer cerca de 0
        const double toleranciaLinea = 5.0;  // puntos de tolerancia para agrupar palabras en línea

        // 1) Detectar marcadores por coordenadas (Y-up nativo de PdfPig)
        // Guardamos también en coordenadas Y-DOWN (desde arriba) para filtrar palabras,
        // ya que GetWords() de PdfPig devuelve BoundingBox en Y-up pero necesitamos
        // consistencia. Usamos Top (Y-up) para todo.
        var marcadores = new List<(int Pagina, int Numero, double TopYUp, double PageHeight)>();
        var dupsCheck = marcadores.GroupBy(m => m.Numero).Where(g => g.Count() > 1);
        foreach (var d in dupsCheck)
            foreach (var m in d)
                System.Diagnostics.Debug.WriteLine($"DUP: P{m.Numero} Pag={m.Pagina} Top={m.TopYUp:F1}");
        foreach (var page in doc.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                double topYUp = word.BoundingBox.Top; // Y-up: mayor = más arriba
                if (Regex.IsMatch(word.Text, @"^\d{1,2}$") &&
                    word.BoundingBox.Left < xMargenNumero &&
                    topYUp > topFooterMax)
                {
                    marcadores.Add((page.Number, int.Parse(word.Text), topYUp, page.Height));
                }
            }
        }

        // Ordenar: por página asc, luego por Top DESC (arriba primero)
        marcadores = marcadores
            .OrderBy(m => m.Pagina)
            .ThenByDescending(m => m.TopYUp)
            .ToList();

        // 2) Construir índice de palabras por página (todas las palabras del doc)
        var palabrasPorPagina = new Dictionary<int, List<(string Texto, double X0, double TopYUp)>>();
        foreach (var page in doc.GetPages())
        {
            var lista = new List<(string, double, double)>();
            foreach (var word in page.GetWords())
                lista.Add((word.Text, word.BoundingBox.Left, word.BoundingBox.Top));
            palabrasPorPagina[page.Number] = lista;
        }

        // 3) Detectar casos compartidos en texto completo
        var textoCompleto = string.Join(" ", doc.GetPages().Select(p => p.Text));
        var regexAvisoCaso = new Regex(
            @"Lea la siguiente situaci[oó]n y responda las preguntas\s+([\d,\sy]+)\.",
            RegexOptions.IgnoreCase);
        int casoIdx = 0;
        foreach (Match m in regexAvisoCaso.Matches(textoCompleto))
        {
            var numeros = Regex.Matches(m.Groups[1].Value, @"\d+")
                              .Select(x => int.Parse(x.Value)).ToList();
            if (numeros.Count == 0) continue;
            casos.Add(new CasoTemp { Texto = "", PaginaInicioPdf = null });
            foreach (var num in numeros) preguntaACaso[num] = casoIdx;
            casoIdx++;
        }

        // Regex para alternativas: letra a/b/c al inicio de línea seguida de mayúscula o comilla
        var regexAlt = new Regex(@"(?m)^([abc])\s+(?=[A-ZÁÉÍÓÚÑ¿""\u201c])");

        // 4) Para cada marcador, extraer texto del bloque y parsear
        for (int i = 0; i < marcadores.Count; i++)
        {
            var actual = marcadores[i];
            var siguiente = (i + 1 < marcadores.Count) ? marcadores[i + 1] : ((int, int, double, double)?)null;

            // Extraer texto del bloque de esta pregunta
            string desde = ExtraerTextoBloque(
                palabrasPorPagina, actual, siguiente, topFooterMax, toleranciaLinea);

            // Limpiar número de pregunta y encabezados al inicio
            desde = Regex.Replace(desde, $@"^{actual.Numero}\s*\n?", "");
            desde = Regex.Replace(desde, @"(?m)^[^\n]*A\d{2}-EBRS?-\d{2}[^\n]*\n?", "");
            desde = desde.Trim();

            if (string.IsNullOrWhiteSpace(desde)) continue;

            // Detectar alternativas
            var altMatches = regexAlt.Matches(desde).Cast<Match>()
                .GroupBy(m => m.Groups[1].Value)
                .Select(g => g.First())
                .OrderBy(m => m.Index)
                .ToList();

            bool necesitaRevision = altMatches.Count != 3;

            // Extraer enunciado
            string enunciado = altMatches.Count > 0
                ? desde.Substring(0, altMatches[0].Index).Trim()
                : desde.Trim();

            if (string.IsNullOrWhiteSpace(enunciado)) continue;

            var pregunta = new PreguntaTemp
            {
                Numero = actual.Numero,
                Texto = LimpiarTexto(enunciado),
                PaginaPdf = actual.Pagina,
                NecesitaRevision = necesitaRevision,
                NotaRevision = necesitaRevision ? "Revisar manualmente" : null,
                CasoIndexTemporal = preguntaACaso.ContainsKey(actual.Numero)
                    ? preguntaACaso[actual.Numero] : null,
                Alternativas = new List<Alternativa>()
            };

            for (int k = 0; k < altMatches.Count; k++)
            {
                string letra = altMatches[k].Groups[1].Value.ToUpper();
                int inicio = altMatches[k].Index + altMatches[k].Length;
                int fin = (k + 1 < altMatches.Count) ? altMatches[k + 1].Index : desde.Length;
                string textoAlt = desde.Substring(inicio, fin - inicio).Trim();

                pregunta.Alternativas.Add(new Alternativa
                {
                    Letra = letra,
                    Texto = LimpiarTexto(textoAlt),
                    EsCorrecta = false
                });
            }

            preguntas.Add(pregunta);
        }

        return (new Cuadernillo { Codigo = codigo }, preguntas, casos);
    }

    // Extrae y reconstruye el texto de un bloque de pregunta agrupando palabras por línea
    private string ExtraerTextoBloque(
        Dictionary<int, List<(string Texto, double X0, double TopYUp)>> palabrasPorPagina,
        (int Pagina, int Numero, double TopYUp, double PageHeight) actual,
        (int Pagina, int Numero, double TopYUp, double PageHeight)? siguiente,
        double topFooterMax,
        double toleranciaLinea)
    {
        var resultado = new System.Text.StringBuilder();

        // Páginas a procesar
        int paginaInicio = actual.Pagina;
        int paginaFin = siguiente.HasValue ? siguiente.Value.Pagina : actual.Pagina;

        for (int pag = paginaInicio; pag <= paginaFin; pag++)
        {
            if (!palabrasPorPagina.ContainsKey(pag)) continue;
            var palabras = palabrasPorPagina[pag];

            // Determinar rango Y-up para esta página
            double topMin, topMax;

            if (pag == paginaInicio)
            {
                topMax = actual.TopYUp;      // desde el marcador hacia abajo
                topMin = topFooterMax;        // hasta el footer
                if (siguiente.HasValue && siguiente.Value.Pagina == pag)
                    topMin = siguiente.Value.TopYUp + 2.0; // cortar en siguiente marcador
            }
            else if (siguiente.HasValue && pag == siguiente.Value.Pagina)
            {
                topMax = actual.PageHeight;   // toda la página desde arriba
                topMin = siguiente.Value.TopYUp + 2.0; // hasta el siguiente marcador
            }
            else
            {
                topMax = actual.PageHeight;
                topMin = topFooterMax;
            }

            // Filtrar palabras en rango Y-up: topMin < Top <= topMax
            var palabrasFiltradas = palabras
                .Where(p => p.TopYUp <= topMax && p.TopYUp > topMin)
                .ToList();

            // Agrupar por línea (tolerancia de 5 puntos en Y)
            var lineas = new List<List<(string Texto, double X0, double TopYUp)>>();
            foreach (var w in palabrasFiltradas.OrderByDescending(p => p.TopYUp))
            {
                var lineaExistente = lineas.FirstOrDefault(
                    l => Math.Abs(l[0].TopYUp - w.TopYUp) <= toleranciaLinea);
                if (lineaExistente != null)
                    lineaExistente.Add(w);
                else
                    lineas.Add(new List<(string, double, double)> { w });
            }

            // Ordenar líneas de arriba a abajo, palabras de izquierda a derecha
            foreach (var linea in lineas.OrderByDescending(l => l[0].TopYUp))
            {
                var textoLinea = string.Join(" ", linea.OrderBy(w => w.X0).Select(w => w.Texto));
                resultado.AppendLine(textoLinea);
            }
        }

        return resultado.ToString();
    }

    private string LimpiarTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        return Regex.Replace(texto, @"\s+", " ").Trim();
    }

    public class PreguntaTemp
    {
        public int Numero { get; set; }
        public string Texto { get; set; } = "";
        public int? PaginaPdf { get; set; }
        public int? CasoId { get; set; }
        public int? CasoIndexTemporal { get; set; }
        public bool NecesitaRevision { get; set; }
        public string? NotaRevision { get; set; }
        public List<Alternativa> Alternativas { get; set; } = new();
    }

    public class CasoTemp
    {
        public int Id { get; set; }
        public string Texto { get; set; } = "";
        public int? PaginaInicioPdf { get; set; }
    }

        // Helper: encuentra en qué página aparece un fragmento de texto
    private int EncontrarPaginaDeTexto(List<(int Pagina, string Texto)> bloques, string fragmento)
    {
        if (string.IsNullOrWhiteSpace(fragmento)) return 1;
        foreach (var b in bloques)
        {
            if (b.Texto.Contains(fragmento, StringComparison.OrdinalIgnoreCase))
                return b.Pagina;
        }
        return 1;
    }
    // DIAGNÓSTICO TEMPORAL - borrar después


    
    // ============================================================
    // Clases temporales auxiliares (agrégalas si no las tienes)
    // ============================================================


    public class CuadernilloMetadatosDto { public string Codigo { get; set; } = ""; public string Area { get; set; } = ""; public string Proposito { get; set; } = ""; public string Convocatoria { get; set; } = ""; }
    public class ResultadoImportacionDto
    {
        public bool Exitoso { get; set; }
        public string Mensaje { get; set; } = "";

        // AGREGA ESTA LÍNEA QUE TE FALTA:
        public int CuadernilloId { get; set; }
    }
}