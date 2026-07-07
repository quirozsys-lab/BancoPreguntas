using BancoPreguntas.Core.Models;
using Dapper;
using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BancoPreguntas.Data.Repositories;

public class AdminRepository(string connectionString)
{
    private NpgsqlConnection Conn() => new(connectionString);

    // ── Cuadernillos ─────────────────────────────────────────────────────────
    public async Task<List<Cuadernillo>> ObtenerTodosAsync() =>
        (await Conn().QueryAsync<Cuadernillo>(
            @"SELECT * FROM ""Cuadernillo"" ORDER BY ""Convocatoria"" DESC, ""Area""")).AsList();

    public async Task<bool> ExisteCuadernilloAsync(string codigo) =>
        await Conn().ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""Cuadernillo"" WHERE ""Codigo"" = @codigo", new { codigo }) > 0;

    public async Task<int> InsertarCuadernilloAsync(Cuadernillo c) =>
        await Conn().ExecuteScalarAsync<int>(
            @"INSERT INTO ""Cuadernillo""
                (""Codigo"",""Area"",""Nivel"",""Proposito"",""Convocatoria"",""Forma"",
                 ""TiempoMinutos"",""TotalPreguntas"",""RutaPdf"",""Activo"",""FechaCreacion"")
              VALUES
                (@Codigo,@Area,@Nivel,@Proposito,@Convocatoria,@Forma,
                 @TiempoMinutos,@TotalPreguntas,@RutaPdf,true,NOW())
              RETURNING ""Id"";", c);

    public async Task CambiarEstadoAsync(int id, bool activo) =>
        await Conn().ExecuteAsync(
            @"UPDATE ""Cuadernillo"" SET ""Activo""=@activo WHERE ""Id""=@id", new { id, activo });

    public async Task EliminarCuadernilloAsync(int id) =>
        await Conn().ExecuteAsync(
            @"DELETE FROM ""Cuadernillo"" WHERE ""Id""=@id", new { id });

    // ── Casos ────────────────────────────────────────────────────────────────
    public async Task<int> InsertarCasoAsync(Caso caso) =>
        await Conn().ExecuteScalarAsync<int>(
            @"INSERT INTO ""Caso"" (""CuadernilloId"", ""Texto"", ""PaginaInicioPdf"")
              VALUES (@CuadernilloId, @Texto, @PaginaInicioPdf)
              RETURNING ""Id"";", caso);

    public async Task<List<Caso>> ObtenerCasosAsync(int cuadernilloId) =>
        (await Conn().QueryAsync<Caso>(
            @"SELECT * FROM ""Caso"" WHERE ""CuadernilloId"" = @cuadernilloId ORDER BY ""Id""",
            new { cuadernilloId })).AsList();

    public async Task<Caso?> ObtenerCasoAsync(int id) =>
        await Conn().QueryFirstOrDefaultAsync<Caso>(
            @"SELECT * FROM ""Caso"" WHERE ""Id"" = @id", new { id });

    public async Task ActualizarCasoAsync(Caso caso) =>
        await Conn().ExecuteAsync(
            @"UPDATE ""Caso"" SET ""Texto""=@Texto, ""PaginaInicioPdf""=@PaginaInicioPdf, ""ImagenPath""=@ImagenPath
              WHERE ""Id""=@Id", caso);

    // ── Preguntas ─────────────────────────────────────────────────────────────
    public async Task<int> InsertarPreguntaAsync(Pregunta p) =>
        await Conn().ExecuteScalarAsync<int>(
            @"INSERT INTO ""Pregunta""
                (""CuadernilloId"",""Numero"",""Texto"",""PaginaPdf"",""NecesitaRevision"",""NotaRevision"",""ImagenPath"",""CasoId"")
              VALUES
                (@CuadernilloId,@Numero,@Texto,@PaginaPdf,@NecesitaRevision,@NotaRevision,@ImagenPath,@CasoId)
              RETURNING ""Id"";", p);

    public async Task ActualizarPreguntaAsync(Pregunta p) =>
        await Conn().ExecuteAsync(
            @"UPDATE ""Pregunta""
              SET ""Texto""=@Texto, ""NecesitaRevision""=@NecesitaRevision,
                  ""NotaRevision""=@NotaRevision, ""ImagenPath""=@ImagenPath, ""CasoId""=@CasoId
              WHERE ""Id""=@Id", p);

    public async Task<List<Pregunta>> ObtenerPreguntasAsync(int cuadernilloId)
    {
        using var conn = Conn();
        var preguntas = (await conn.QueryAsync<Pregunta>(
            @"SELECT * FROM ""Pregunta"" WHERE ""CuadernilloId"" = @cuadernilloId ORDER BY ""Numero""",
            new { cuadernilloId })).AsList();

        if (!preguntas.Any()) return preguntas;

        var ids = preguntas.Select(p => p.Id).ToArray();
        var alts = (await conn.QueryAsync<Alternativa>(
            @"SELECT * FROM ""Alternativa"" WHERE ""PreguntaId"" = ANY(@ids) ORDER BY ""PreguntaId"", ""Letra""",
            new { ids })).ToLookup(a => a.PreguntaId);

        foreach (var p in preguntas)
            p.Alternativas = alts[p.Id].ToList();

        return preguntas;
    }

    public async Task<List<Pregunta>> ObtenerPreguntasRevisionAsync(int cuadernilloId)
    {
        using var conn = Conn();
        var preguntas = (await conn.QueryAsync<Pregunta>(
            @"SELECT * FROM ""Pregunta""
              WHERE ""CuadernilloId""=@cuadernilloId AND ""NecesitaRevision""=true
              ORDER BY ""Numero""",
            new { cuadernilloId })).AsList();

        if (!preguntas.Any()) return preguntas;

        var ids = preguntas.Select(p => p.Id).ToArray();
        var alts = (await conn.QueryAsync<Alternativa>(
            @"SELECT * FROM ""Alternativa"" WHERE ""PreguntaId"" = ANY(@ids) ORDER BY ""PreguntaId"", ""Letra""",
            new { ids })).ToLookup(a => a.PreguntaId);

        foreach (var p in preguntas)
            p.Alternativas = alts[p.Id].ToList();

        return preguntas;
    }

    public async Task<int> ContarRevisionAsync(int cuadernilloId) =>
        await Conn().ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""Pregunta"" WHERE ""CuadernilloId""=@cuadernilloId AND ""NecesitaRevision""=true",
            new { cuadernilloId });

    public async Task<string?> ObtenerRutaPdfAsync(int cuadernilloId) =>
        await Conn().ExecuteScalarAsync<string?>(
            @"SELECT ""RutaPdf"" FROM ""Cuadernillo"" WHERE ""Id""=@cuadernilloId",
            new { cuadernilloId });

    // ── Alternativas ──────────────────────────────────────────────────────────
    public async Task InsertarAlternativaAsync(Alternativa a) =>
        await Conn().ExecuteAsync(
            @"INSERT INTO ""Alternativa"" (""PreguntaId"",""Letra"",""Texto"",""EsCorrecta"",""ImagenPath"")
              VALUES (@PreguntaId,@Letra,@Texto,@EsCorrecta,@ImagenPath);", a);

    public async Task ActualizarAlternativaAsync(Alternativa a) =>
        await Conn().ExecuteAsync(
            @"UPDATE ""Alternativa""
              SET ""Texto""=@Texto, ""EsCorrecta""=@EsCorrecta, ""ImagenPath""=@ImagenPath
              WHERE ""Id""=@Id", a);

    public async Task EliminarAlternativaAsync(int id) =>
        await Conn().ExecuteAsync(
            @"DELETE FROM ""Alternativa"" WHERE ""Id""=@id", new { id });
}