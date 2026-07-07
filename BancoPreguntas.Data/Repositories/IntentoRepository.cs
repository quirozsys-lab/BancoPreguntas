using BancoPreguntas.Core.Models;
using Dapper;
using Npgsql;

namespace BancoPreguntas.Data.Repositories;

public class IntentoRepository(string connectionString)
{
    private NpgsqlConnection Conn() => new(connectionString);

    public async Task<int> CrearAsync(int cuadernilloId, string nombre) =>
        await Conn().ExecuteScalarAsync<int>(
            @"INSERT INTO ""Intento"" (""CuadernilloId"",""NombreEvaluado"",""FechaInicio"",""Estado"")
              VALUES (@cuadernilloId,@nombre,NOW(),'EnCurso')
              RETURNING ""Id"";",
            new { cuadernilloId, nombre });

    public async Task<Intento?> ObtenerAsync(int intentoId) =>
        await Conn().QueryFirstOrDefaultAsync<Intento>(
            @"SELECT I.*, C.""Codigo"" AS CodigoCuadernillo, C.""TotalPreguntas"" AS TotalPosible
              FROM ""Intento"" I
              INNER JOIN ""Cuadernillo"" C ON C.""Id""=I.""CuadernilloId""
              WHERE I.""Id""=@intentoId",
            new { intentoId });

    // MERGE no existe en PostgreSQL → usar INSERT ... ON CONFLICT DO UPDATE
    public async Task GuardarRespuestaAsync(int intentoId, int preguntaId, int? alternativaId) =>
        await Conn().ExecuteAsync(
            @"INSERT INTO ""Respuesta"" (""IntentoId"",""PreguntaId"",""AlternativaId"")
              VALUES (@intentoId,@preguntaId,@alternativaId)
              ON CONFLICT (""IntentoId"",""PreguntaId"")
              DO UPDATE SET ""AlternativaId""=EXCLUDED.""AlternativaId"";",
            new { intentoId, preguntaId, alternativaId });

    // EXEC sp_... → SELECT sp_...() en PostgreSQL
    public async Task<Intento?> FinalizarAsync(int intentoId)
    {
        using var conn = Conn();
        await conn.ExecuteAsync(
            @"SELECT sp_FinalizarIntento(@intentoId);",
            new { intentoId });
        return await conn.QueryFirstOrDefaultAsync<Intento>(
            @"SELECT I.*, C.""Codigo"" AS CodigoCuadernillo, C.""TotalPreguntas"" AS TotalPosible
              FROM ""Intento"" I
              INNER JOIN ""Cuadernillo"" C ON C.""Id""=I.""CuadernilloId""
              WHERE I.""Id""=@intentoId",
            new { intentoId });
    }

    public async Task ExpirarAsync(int intentoId) =>
        await Conn().ExecuteAsync(
            @"UPDATE ""Intento"" SET ""Estado""='Expirado',""FechaFin""=NOW() WHERE ""Id""=@intentoId",
            new { intentoId });

    public async Task<ResultadoVm?> ObtenerResultadoAsync(int intentoId)
    {
        using var conn = Conn();

        var intento = await conn.QueryFirstOrDefaultAsync<Intento>(
            @"SELECT I.*, C.""Codigo"" AS CodigoCuadernillo, C.""TotalPreguntas"" AS TotalPosible
              FROM ""Intento"" I
              INNER JOIN ""Cuadernillo"" C ON C.""Id""=I.""CuadernilloId""
              WHERE I.""Id""=@intentoId",
            new { intentoId });

        if (intento is null) return null;

        var preguntas = (await conn.QueryAsync<Pregunta>(
            @"SELECT ""Id"",""Numero"",""Texto"",""ImagenPath"" FROM ""Pregunta""
              WHERE ""CuadernilloId""=@id ORDER BY ""Numero""",
            new { id = intento.CuadernilloId })).AsList();

        var alternativas = (await conn.QueryAsync<Alternativa>(
            @"SELECT A.""Id"",A.""PreguntaId"",A.""Letra"",A.""Texto"",A.""EsCorrecta""
              FROM ""Alternativa"" A
              INNER JOIN ""Pregunta"" P ON P.""Id""=A.""PreguntaId""
              WHERE P.""CuadernilloId""=@id",
            new { id = intento.CuadernilloId })).ToLookup(a => a.PreguntaId);

        var respuestas = (await conn.QueryAsync(
            @"SELECT ""PreguntaId"",""AlternativaId"",""EsCorrecta"" FROM ""Respuesta""
              WHERE ""IntentoId""=@intentoId",
            new { intentoId }))
            .ToDictionary(r => (int)r.PreguntaId);

        var lista = new List<PreguntaResultadoVm>();
        foreach (var p in preguntas)
        {
            p.Alternativas = alternativas[p.Id].ToList();
            var altCorrecta = p.Alternativas.FirstOrDefault(a => a.EsCorrecta);
            respuestas.TryGetValue(p.Id, out var resp);
            int? altSelId = resp is not null ? (int?)resp.AlternativaId : null;
            var altSel = altSelId.HasValue ? p.Alternativas.FirstOrDefault(a => a.Id == altSelId) : null;

            lista.Add(new PreguntaResultadoVm
            {
                Pregunta = p,
                LetraSeleccionada = altSel?.Letra,
                LetraCorrecta = altCorrecta?.Letra ?? "?",
                Acerto = resp is not null && resp.EsCorrecta == true,
                NoRespondio = resp is null || altSelId is null
            });
        }

        return new ResultadoVm { Intento = intento, Preguntas = lista };
    }
}