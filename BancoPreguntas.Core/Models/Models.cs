using System;
using System.Collections.Generic;

namespace BancoPreguntas.Core.Models;

// ── 1. NUEVO MODELO DE CASOS / SITUACIONES COMPARTIDAS ────────────────────────
public class ResultadoImportacion
{
    public bool Exitoso { get; set; } = true;
    public string Mensaje { get; set; } = string.Empty;
    public int CuadernilloId { get; set; }
    public int TotalPreguntas { get; set; }
    public int PreguntasLimpias { get; set; }
    public int PreguntasRevision { get; set; }
    public List<string> Advertencias { get; set; } = new();
}
public class Caso
{
    public int Id { get; set; }
    public int CuadernilloId { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? ImagenPath { get; set; }
    public int PaginaInicioPdf { get; set; }
}

// ── 2. MODELO CUADERNILLO ────────────────────────────────────────────────────
public class Cuadernillo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Nivel { get; set; } = string.Empty;
    public string Proposito { get; set; } = string.Empty;
    public string Convocatoria { get; set; } = string.Empty;
    public string Forma { get; set; } = "1";
    public int TiempoMinutos { get; set; } = 180;
    public int TotalPreguntas { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }

    // Ruta relativa al PDF original guardado en wwwroot/cuadernillos/
    public string? RutaPdf { get; set; }
}

// ── 3. MODELO PREGUNTA (ACTUALIZADO CON CASOID Y PAGINAPDF) ───────────────────
public class Pregunta
{
    public int Id { get; set; }
    public int CuadernilloId { get; set; }
    public int Numero { get; set; }
    public string Texto { get; set; } = string.Empty;

    // Cambiado a obligatorio (no nullable) para mapear la página física del PDF
    public int PaginaPdf { get; set; } = 1;

    public bool NecesitaRevision { get; set; }
    public string? NotaRevision { get; set; }
    public string? ImagenPath { get; set; }

    // Llave foránea opcional que apunta a un Caso/Lectura compartida
    public int? CasoId { get; set; }

    // Relación de composición con alternativas
    public List<Alternativa> Alternativas { get; set; } = new();
}

// ── 4. MODELO ALTERNATIVA (ACTUALIZADO CON IMAGENPATH) ───────────────────────
public class Alternativa
{
    public int Id { get; set; }
    public int PreguntaId { get; set; }
    public string Letra { get; set; } = string.Empty; // a, b, c
    public string Texto { get; set; } = string.Empty;
    public bool EsCorrecta { get; set; }

    // Ruta de la imagen opcional de la opción si es un gráfico o fórmula matemática
    public string? ImagenPath { get; set; }
}

// ── 5. MODELO INTENTO ────────────────────────────────────────────────────────
public class Intento
{
    public int Id { get; set; }
    public int CuadernilloId { get; set; }
    public string NombreEvaluado { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string Estado { get; set; } = "EnCurso";
    public int? Puntaje { get; set; }
    public int? TotalRespondidas { get; set; }
    public string TituloCuadernillo { get; set; } = string.Empty;
    public int TotalPosible { get; set; }
}

// ── 6. VIEW MODELS (VISTAS DE FRONTEND EN BLAZOR) ────────────────────────────
public class ExamenVm
{
    public int IntentoId { get; set; }
    public Cuadernillo Cuadernillo { get; set; } = new();
    public List<Pregunta> Preguntas { get; set; } = new();
    public List<Caso> Casos { get; set; } = new(); // ← agregar
}

public class ResultadoVm
{
    public Intento Intento { get; set; } = new();
    public List<PreguntaResultadoVm> Preguntas { get; set; } = new();
}

public class PreguntaResultadoVm
{
    public Pregunta Pregunta { get; set; } = new();
    public string? LetraSeleccionada { get; set; }
    public string LetraCorrecta { get; set; } = string.Empty;
    public bool Acerto { get; set; }
    public bool NoRespondio { get; set; }
}

// ── 7. CLASE TEMPORAL DE APOYO PARA EL IMPORTADOR DE TEXTO ───────────────────
public class PreguntaTemp : Pregunta
{
    // Esta propiedad acumula el bloque situacional antes de registrar el Caso en BD
    public string? TextoCasoAsociado { get; set; }
}