-- ============================================================
--  BANCO DE PREGUNTAS — Script SQL completo
--  SQL Server 2019+
--  Ejecutar ANTES de arrancar la aplicación por primera vez.
--  Las tablas de ASP.NET Identity se crean automáticamente
--  vía EF Core Migrations al iniciar la app.
-- ============================================================

CREATE DATABASE BancoPreguntas;
GO
USE BancoPreguntas;
GO

-- ── 1. Cuadernillo ────────────────────────────────────────────────────────────
CREATE TABLE Cuadernillo (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Codigo         NVARCHAR(30)  NOT NULL,
    Titulo         NVARCHAR(300) NOT NULL,
    Area           NVARCHAR(100) NOT NULL,
    Nivel          NVARCHAR(50)  NOT NULL,
    Proposito      NVARCHAR(100) NOT NULL, -- Concurso de Ascenso | Nombramiento Docente | Reasignación | Contrato Docente
    Convocatoria   NVARCHAR(10)  NOT NULL,
    Forma          NVARCHAR(5)   NOT NULL DEFAULT '1',
    TiempoMinutos  INT           NOT NULL DEFAULT 180,
    TotalPreguntas INT           NOT NULL DEFAULT 0,
    Activo         BIT           NOT NULL DEFAULT 1,
    FechaCreacion  DATETIME2     NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_Cuadernillo UNIQUE (Codigo, Forma)
);
GO

-- ── 2. Pregunta ───────────────────────────────────────────────────────────────
CREATE TABLE Pregunta (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    CuadernilloId    INT           NOT NULL REFERENCES Cuadernillo(Id) ON DELETE CASCADE,
    Numero           INT           NOT NULL,
    Texto            NVARCHAR(MAX) NOT NULL,
    ImagenPath       NVARCHAR(500) NULL,       -- ruta relativa al wwwroot
    NecesitaRevision BIT           NOT NULL DEFAULT 0,
    NotaRevision     NVARCHAR(500) NULL,
    CONSTRAINT UQ_Pregunta UNIQUE (CuadernilloId, Numero)
);
GO

-- ── 3. Alternativa ────────────────────────────────────────────────────────────
CREATE TABLE Alternativa (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    PreguntaId INT           NOT NULL REFERENCES Pregunta(Id) ON DELETE CASCADE,
    Letra      CHAR(1)       NOT NULL,         -- a | b | c
    Texto      NVARCHAR(MAX) NOT NULL,
    EsCorrecta BIT           NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Alternativa UNIQUE (PreguntaId, Letra)
);
GO

-- ── 4. Intento ────────────────────────────────────────────────────────────────
CREATE TABLE Intento (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    CuadernilloId    INT           NOT NULL REFERENCES Cuadernillo(Id),
    NombreEvaluado   NVARCHAR(200) NOT NULL,
    FechaInicio      DATETIME2     NOT NULL DEFAULT GETDATE(),
    FechaFin         DATETIME2     NULL,
    Estado           NVARCHAR(20)  NOT NULL DEFAULT 'EnCurso', -- EnCurso | Finalizado | Expirado
    Puntaje          INT           NULL,
    TotalRespondidas INT           NULL
);
GO

-- ── 5. Respuesta ─────────────────────────────────────────────────────────────
CREATE TABLE Respuesta (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    IntentoId     INT NOT NULL REFERENCES Intento(Id)     ON DELETE CASCADE,
    PreguntaId    INT NOT NULL REFERENCES Pregunta(Id),
    AlternativaId INT NULL     REFERENCES Alternativa(Id),
    EsCorrecta    BIT NULL,
    CONSTRAINT UQ_Respuesta UNIQUE (IntentoId, PreguntaId)
);
GO

-- ── Índices ───────────────────────────────────────────────────────────────────
CREATE INDEX IX_Pregunta_Cuad   ON Pregunta(CuadernilloId);
CREATE INDEX IX_Alt_Pregunta    ON Alternativa(PreguntaId);
CREATE INDEX IX_Intento_Cuad    ON Intento(CuadernilloId);
CREATE INDEX IX_Intento_Estado  ON Intento(Estado);
CREATE INDEX IX_Respuesta_Int   ON Respuesta(IntentoId);
GO

-- ── SP: Finalizar intento ────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_FinalizarIntento
    @IntentoId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Marcar respuestas correctas e incorrectas
    UPDATE R
    SET    R.EsCorrecta = A.EsCorrecta
    FROM   Respuesta R
    INNER  JOIN Alternativa A ON A.Id = R.AlternativaId
    WHERE  R.IntentoId = @IntentoId;

    -- Las no respondidas (AlternativaId NULL) cuentan como incorrectas
    UPDATE Respuesta
    SET    EsCorrecta = 0
    WHERE  IntentoId = @IntentoId AND AlternativaId IS NULL;

    -- Calcular totales
    DECLARE @Puntaje INT, @Total INT;
    SELECT  @Puntaje = SUM(CASE WHEN EsCorrecta = 1 THEN 1 ELSE 0 END),
            @Total   = COUNT(*)
    FROM    Respuesta
    WHERE   IntentoId = @IntentoId;

    UPDATE  Intento
    SET     Estado           = 'Finalizado',
            FechaFin         = GETDATE(),
            Puntaje          = @Puntaje,
            TotalRespondidas = @Total
    WHERE   Id = @IntentoId;
END;
GO

-- ── Dato inicial de prueba ───────────────────────────────────────────────────
-- (el cuadernillo real se carga desde el panel admin / importación)
INSERT INTO Cuadernillo (Codigo,Titulo,Area,Nivel,Proposito,Convocatoria,Forma,TiempoMinutos,TotalPreguntas)
VALUES ('A40-EBRS-11',
        'Concurso de Ascenso EBR — Secundaria Matemática 2023',
        'Matemática','Secundaria','Concurso de Ascenso','2023','1',180,60);
GO

PRINT '✅ Base de datos BancoPreguntas creada correctamente.';
PRINT '   Las tablas de Identity (AspNetUsers, etc.) se crean automáticamente al iniciar la app.';
GO
