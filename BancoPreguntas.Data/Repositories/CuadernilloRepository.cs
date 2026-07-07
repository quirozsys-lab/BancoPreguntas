using BancoPreguntas.Core.Models;
using Dapper;
using Npgsql;

namespace BancoPreguntas.Data.Repositories;

public class CuadernilloRepository(string connectionString)
{
    private NpgsqlConnection Conn() => new(connectionString);

    public async Task<List<string>> ObtenerAreasAsync() =>
        (await Conn().QueryAsync<string>(
            @"SELECT DISTINCT ""Area"" FROM ""Cuadernillo"" WHERE ""Activo""=true ORDER BY ""Area""")).AsList();

    public async Task<List<string>> ObtenerPropositosAsync(string area) =>
        (await Conn().QueryAsync<string>(
            @"SELECT DISTINCT ""Proposito"" FROM ""Cuadernillo"" WHERE ""Activo""=true AND ""Area""=@area ORDER BY ""Proposito""",
            new { area })).AsList();

    public async Task<List<string>> ObtenerConvocatoriasAsync(string area, string proposito) =>
        (await Conn().QueryAsync<string>(
            @"SELECT DISTINCT ""Convocatoria"" FROM ""Cuadernillo""
              WHERE ""Activo""=true AND ""Area""=@area AND ""Proposito""=@proposito
              ORDER BY ""Convocatoria"" DESC",
            new { area, proposito })).AsList();

    public async Task<List<Cuadernillo>> ObtenerFiltradosAsync(string area, string proposito, string convocatoria) =>
        (await Conn().QueryAsync<Cuadernillo>(
            @"SELECT ""Id"",""Codigo"",""Area"",""Nivel"",""Proposito"",""Convocatoria"",""TiempoMinutos"",""TotalPreguntas""
              FROM ""Cuadernillo""
              WHERE ""Activo""=true AND ""Area""=@area AND ""Proposito""=@proposito AND ""Convocatoria""=@convocatoria
              ORDER BY ""Codigo""",
            new { area, proposito, convocatoria })).AsList();

    public async Task<ExamenVm?> ObtenerExamenAsync(int cuadernilloId)
    {
        using var conn = Conn();

        var cuadernillo = await conn.QueryFirstOrDefaultAsync<Cuadernillo>(
            @"SELECT * FROM ""Cuadernillo"" WHERE ""Id""=@cuadernilloId AND ""Activo""=true",
            new { cuadernilloId });

        if (cuadernillo is null) return null;

        var preguntas = (await conn.QueryAsync<Pregunta>(
            @"SELECT ""Id"", ""Numero"", ""Texto"", ""ImagenPath"", ""CasoId"" FROM ""Pregunta"" 
              WHERE ""CuadernilloId""=@cuadernilloId ORDER BY ""Numero""",
            new { cuadernilloId })).AsList();

        var alternativas = (await conn.QueryAsync<Alternativa>(
            @"SELECT A.""Id"", A.""PreguntaId"", A.""Letra"", A.""Texto"", A.""ImagenPath""
              FROM ""Alternativa"" A
              INNER JOIN ""Pregunta"" P ON P.""Id"" = A.""PreguntaId""
              WHERE P.""CuadernilloId"" = @cuadernilloId
              ORDER BY A.""PreguntaId"", A.""Letra""",
            new { cuadernilloId })).ToLookup(a => a.PreguntaId);

        foreach (var p in preguntas)
            p.Alternativas = alternativas[p.Id].ToList();

        var casos = (await conn.QueryAsync<Caso>(
            @"SELECT * FROM ""Caso"" WHERE ""CuadernilloId"" = @cuadernilloId",
            new { cuadernilloId })).AsList();

        return new ExamenVm
        {
            Cuadernillo = cuadernillo,
            Preguntas = preguntas,
            Casos = casos
        };
    }
}