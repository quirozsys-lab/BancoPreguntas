# Banco de Preguntas — Simulacro Docente MINEDU

Sistema web para que docentes practiquen exámenes del MINEDU
(Concurso de Ascenso, Nombramiento, etc.) con cuadernillos reales.

## Stack
- **Frontend/Backend:** Blazor Server (.NET 9)
- **UI:** MudBlazor 7
- **Data:** Dapper + SQL Server (dev) → Neon.tech PostgreSQL (prod)
- **Auth:** ASP.NET Identity (EF Core, solo para admin)
- **PDF:** PdfPig + PDFiumCore
- **Deploy:** Railway + Docker

---

## Arranque local (primera vez)

### 1. Crear la base de datos
Ejecuta `BancoPreguntas_DB.sql` en SQL Server local.

### 2. Restaurar paquetes
```bash
cd BancoPreguntas.Web
dotnet restore
```

### 3. Crear tablas de Identity (EF Core Migrations)
```bash
dotnet ef migrations add InitialIdentity --project ../BancoPreguntas.Data --startup-project .
dotnet ef database update --project ../BancoPreguntas.Data --startup-project .
```

### 4. Ejecutar
```bash
dotnet run
```

La app queda en `https://localhost:5001`.
El usuario admin inicial es: `admin@bancodepreguntas.pe` / `Admin1234!`
(Cámbialo en `appsettings.json` → sección `AdminInicial`)

---

## Flujo de uso

### Importar un cuadernillo
1. Entra a `/Account/Login` con las credenciales de admin
2. Ve a **Importar** en el menú lateral
3. Sube el PDF del cuadernillo + PDF de la hoja de respuestas
4. Selecciona el propósito (Concurso de Ascenso, Nombramiento, etc.)
5. Haz clic en **Importar cuadernillo**
6. Si hay preguntas con fórmulas/gráficos, ve a **Revisión** y corrígelas manualmente

### Rendir un examen (docente)
1. Entra a `/` (página principal)
2. Ingresa tu nombre
3. Selecciona: Área → Propósito → Año de convocatoria → Cuadernillo
4. Haz clic en **Iniciar examen**
5. Responde las preguntas usando el mapa de preguntas y la navegación
6. Al finalizar (o al agotarse el tiempo), se muestra el puntaje y la revisión detallada

---

## Deploy en Railway

1. Crea un proyecto en [Railway](https://railway.app)
2. Conecta tu repositorio GitHub
3. Railway detecta el `Dockerfile` automáticamente
4. Agrega la variable de entorno:
   ```
   ConnectionStrings__DefaultConnection=Server=...;Database=BancoPreguntas;...
   AdminInicial__Password=TuPasswordSegura
   ```
5. Para la BD puedes usar SQL Server en Railway o migrar a PostgreSQL (Neon.tech)

> **Nota para migrar a PostgreSQL (Neon.tech):**
> Cambia `UseSqlServer` → `UseNpgsql` en `Program.cs`
> y ajusta la cadena de conexión. Las queries Dapper son
> compatibles casi sin cambios (GETDATE() → NOW(), etc.).

---

## Estructura del proyecto
```
BancoPreguntas/
├── BancoPreguntas.Core/       → Modelos del dominio
├── BancoPreguntas.Data/       → Repositorios Dapper + Identity EF Core
├── BancoPreguntas.Services/   → ExamenService + ImportadorService (PDF)
└── BancoPreguntas.Web/        → Blazor Server + MudBlazor
    ├── Pages/
    │   ├── Index.razor        → Selección de examen (público)
    │   ├── Examen.razor       → Pantalla del examen con cronómetro
    │   ├── Resultado.razor    → Puntaje y revisión detallada
    │   ├── Admin/
    │   │   ├── Index.razor    → Dashboard admin
    │   │   ├── Importar.razor → Importar PDF cuadernillo + clave
    │   │   ├── Revision.razor → Corregir preguntas con fórmulas/gráficos
    │   │   └── Estadisticas.razor → Stats por cuadernillo
    │   └── Account/
    │       ├── Login.cshtml   → Login admin
    │       └── Logout.cshtml  → Logout
    └── Shared/
        ├── MainLayout.razor   → Layout público
        └── AdminLayout.razor  → Layout admin con sidebar
```
