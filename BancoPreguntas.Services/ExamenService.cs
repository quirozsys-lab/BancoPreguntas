using BancoPreguntas.Core.Models;
using BancoPreguntas.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BancoPreguntas.Services;

public class ExamenService(
    CuadernilloRepository cuadernilloRepo,
    IntentoRepository     intentoRepo)
{
    public Task<List<string>> ObtenerAreasAsync() =>
        cuadernilloRepo.ObtenerAreasAsync();

    public Task<List<string>> ObtenerPropositosAsync(string area) =>
        cuadernilloRepo.ObtenerPropositosAsync(area);

    public Task<List<string>> ObtenerConvocatoriasAsync(string area, string proposito) =>
        cuadernilloRepo.ObtenerConvocatoriasAsync(area, proposito);

    public Task<List<Cuadernillo>> ObtenerCuadernillosAsync(string area, string proposito, string conv) =>
        cuadernilloRepo.ObtenerFiltradosAsync(area, proposito, conv);

    public async Task<ExamenVm> IniciarAsync(int cuadernilloId, string nombre)
    {
        var examen = await cuadernilloRepo.ObtenerExamenAsync(cuadernilloId)
            ?? throw new InvalidOperationException("Cuadernillo no encontrado.");

        examen.IntentoId = await intentoRepo.CrearAsync(cuadernilloId, nombre.Trim());
        return examen;
    }

    public async Task<ExamenVm?> CargarExamenEnCursoAsync(int intentoId)
    {
        var intento = await intentoRepo.ObtenerAsync(intentoId);
        if (intento is null) { Console.WriteLine("Intento nulo"); return null; }

        Console.WriteLine($"Cargando cuadernillo ID: {intento.CuadernilloId}");

        var examen = await cuadernilloRepo.ObtenerExamenAsync(intento.CuadernilloId);

        if (examen?.Preguntas != null)
            Console.WriteLine($"Preguntas encontradas: {examen.Preguntas.Count}");
        else
            Console.WriteLine("Examen o preguntas nulas");

        return examen;
    }

    public Task<Intento?> ObtenerIntentoAsync(int intentoId) =>
        intentoRepo.ObtenerAsync(intentoId);

    public Task GuardarRespuestaAsync(int intentoId, int preguntaId, int? alternativaId) =>
        intentoRepo.GuardarRespuestaAsync(intentoId, preguntaId, alternativaId);

    public Task<Intento?> FinalizarAsync(int intentoId) =>
        intentoRepo.FinalizarAsync(intentoId);

    public Task ExpirarAsync(int intentoId) =>
        intentoRepo.ExpirarAsync(intentoId);

    public Task<ResultadoVm?> ObtenerResultadoAsync(int intentoId) =>
        intentoRepo.ObtenerResultadoAsync(intentoId);
}
