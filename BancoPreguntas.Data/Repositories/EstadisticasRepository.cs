using BancoPreguntas.Core.Models;
using Dapper;
using Npgsql;

namespace BancoPreguntas.Data.Repositories;

public class EstadisticasCuadernillo
{
    public Cuadernillo Cuadernillo { get; set; } = new();
    public int TotalIntentos { get; set; }
    public int Finalizados { get; set; }
    public int Expirados { get; set; }
    public double PromedioPuntaje { get; set; }
    public int? PuntajeMaximo { get; set; }
    public int? PuntajeMinimo { get; set; }
    public List<RangoPuntaje> Distribucion { get; set; } = new();
    public List<PreguntaDificil> PreguntasDificiles { get; set; } = new();
    public List<Intento> UltimosIntentos { get; set; } = new();
}

public class RangoPuntaje
{
    public int RangoInicio { get; set; }
    public int Cantidad { get; set; }
    public string Etiqueta => $"{RangoInicio} – {RangoInicio + 9}";
}

public class PreguntaDificil
{
    public int Numero { get; set; }
    public string TextoCorto { get; set; } = string.Empty;
    public int TotalRespuestas { get; set; }
    public int TotalErrores { get; set; }
    public decimal PorcentajeError { get; set; }
}

public class EstadisticasRepository(string connectionString)
{
    private NpgsqlConnection Conn() => new(connectionString);

    public async Task<EstadisticasCuadernillo> ObtenerAsync(int cuadernilloId)
    {
        using var conn = Conn();

        var cuadernillo = await conn.QueryFirstOrDefaultAsync<Cuadernillo>(
            @"SELECT * FROM ""Cuadernillo"" WHERE ""Id""=@cuadernilloId",
            new { cuadernilloId }) ?? new();

        var resumen = await conn.QueryFirstOrDefaultAsync(
            @"SELECT
                COUNT(*)                                                    AS TotalIntentos,
                SUM(CASE WHEN ""Estado""='Finalizado' THEN 1 ELSE 0 END)   AS Finalizados,
                SUM(CASE WHEN ""Estado""='Expirado'   THEN 1 ELSE 0 END)   AS Expirados,
                AVG(""Puntaje""::FLOAT)                                     AS PromedioPuntaje,
                MAX(""Puntaje"")                                            AS PuntajeMaximo,
                MIN(CASE WHEN ""Estado"" <> 'EnCurso' THEN ""Puntaje"" END) AS PuntajeMinimo
              FROM ""Intento"" WHERE ""CuadernilloId""=@cuadernilloId",
            new { cuadernilloId });

        var distribucion = (await conn.QueryAsync<RangoPuntaje>(
            @"SELECT FLOOR(""Puntaje""::FLOAT/10)*10 AS RangoInicio, COUNT(*) AS Cantidad
              FROM ""Intento""
              WHERE ""CuadernilloId""=@cuadernilloId AND ""Estado""='Finalizado'
              GROUP BY FLOOR(""Puntaje""::FLOAT/10)*10
              ORDER BY RangoInicio",
            new { cuadernilloId })).AsList();

        // TOP 10 → LIMIT 10 en PostgreSQL
        // LEFT() → SUBSTRING() o LEFT() también funciona en PostgreSQL
        // CAST(...AS DECIMAL) → ::NUMERIC
        var dificiles = (await conn.QueryAsync<PreguntaDificil>(
            @"SELECT
                P.""Numero"",
                LEFT(P.""Texto"", 80)                                                AS TextoCorto,
                COUNT(R.""Id"")                                                       AS TotalRespuestas,
                SUM(CASE WHEN R.""EsCorrecta""=false THEN 1 ELSE 0 END)              AS TotalErrores,
                (SUM(CASE WHEN R.""EsCorrecta""=false THEN 1.0 ELSE 0 END)
                    / NULLIF(COUNT(R.""Id""),0) * 100)::NUMERIC(5,1)                 AS PorcentajeError
              FROM ""Pregunta"" P
              LEFT JOIN ""Respuesta"" R ON R.""PreguntaId""=P.""Id""
              INNER JOIN ""Intento"" I ON I.""Id""=R.""IntentoId"" AND I.""Estado""='Finalizado'
              WHERE P.""CuadernilloId""=@cuadernilloId
              GROUP BY P.""Numero"", P.""Texto""
              ORDER BY PorcentajeError DESC
              LIMIT 10",
            new { cuadernilloId })).AsList();

        var ultimos = (await conn.QueryAsync<Intento>(
            @"SELECT I.*, C.""Codigo"" AS CodigoCuadernillo, C.""TotalPreguntas"" AS TotalPosible
              FROM ""Intento"" I
              INNER JOIN ""Cuadernillo"" C ON C.""Id""=I.""CuadernilloId""
              WHERE I.""CuadernilloId""=@cuadernilloId
              ORDER BY I.""FechaInicio"" DESC
              LIMIT 20",
            new { cuadernilloId })).AsList();

        return new EstadisticasCuadernillo
        {
            Cuadernillo = cuadernillo,
            TotalIntentos = (int)(resumen?.TotalIntentos ?? 0),
            Finalizados = (int)(resumen?.Finalizados ?? 0),
            Expirados = (int)(resumen?.Expirados ?? 0),
            PromedioPuntaje = (double)(resumen?.PromedioPuntaje ?? 0),
            PuntajeMaximo = (int?)resumen?.PuntajeMaximo,
            PuntajeMinimo = (int?)resumen?.PuntajeMinimo,
            Distribucion = distribucion,
            PreguntasDificiles = dificiles,
            UltimosIntentos = ultimos
        };
    }
}