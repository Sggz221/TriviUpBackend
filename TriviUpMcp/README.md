# TriviUpMcp

Servidor [MCP](https://modelcontextprotocol.io/) (Model Context Protocol) que expone el banco
personal de preguntas de TriviUp (`api/banco-preguntas`, `api/banco-categorias`) como tools para
agentes de IA. Es un cliente HTTP más de la API existente: no reemplaza ni modifica `TriviUpBackend`,
solo la envuelve con una interfaz pensada para que un agente cree y mantenga preguntas de forma
conversacional.

## Requisitos

- .NET 9 SDK.
- La API de `TriviUpBackend` corriendo y accesible (local o desplegada).
- Un usuario de TriviUp (username/email + contraseña) con el que autenticarse.

## Configuración

El servidor lee la URL base de la API de la variable de entorno `TRIVIUP_API_URL`. Si no se define,
usa `http://localhost:5164` (el puerto por defecto de `TriviUpBackend` en desarrollo).

```bash
export TRIVIUP_API_URL=https://triviup.up.railway.app
```

## Cómo registrarlo en un cliente MCP

El transporte es **stdio**: el cliente MCP lanza el proceso y le habla por stdin/stdout. Ejemplo de
configuración (Claude Desktop, Claude Code u otro cliente compatible):

```json
{
  "mcpServers": {
    "triviup-banco-preguntas": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/ruta/a/TriviUpBackend/TriviUpMcp"],
      "env": {
        "TRIVIUP_API_URL": "http://localhost:5164"
      }
    }
  }
}
```

Para producción, es más rápido publicar el ejecutable y apuntar `command` directamente al binario
(`dotnet publish` y usar la ruta al `.dll`/ejecutable resultante) en vez de `dotnet run`.

## Flujo esperado para un agente de IA

1. **`login(username, password)`** — siempre la primera llamada. Autentica contra `POST /Auth/signin`
   y guarda el JWT en memoria del proceso para el resto de la sesión. Cualquier otra tool llamada
   antes de `login` devuelve un error claro (`"No autenticado: usa la tool 'login'..."`) en vez de
   fallar de forma críptica.
2. El resto de tools ya usan ese token automáticamente como `Authorization: Bearer <token>`.
3. La sesión vive mientras vive el proceso del MCP: si el cliente lo reinicia, hay que volver a
   llamar a `login`.

Todas las tools devuelven **texto JSON** (nunca lanzan un error de protocolo crudo): si algo falla
—credenciales inválidas, validación de negocio, recurso no encontrado, no autenticado—, el JSON de
respuesta trae `{"error": "...", "statusCode": ...}` con un mensaje legible para que el agente pueda
corregir su siguiente llamada sin necesidad de reintentos a ciegas.

## Reglas de negocio a tener en cuenta

- Una pregunta necesita **al menos 2 respuestas** con texto, y **exactamente una** marcada como
  correcta (`esCorrecta: true`). `crear_pregunta`/`editar_pregunta` validan esto antes de llamar a la
  API para dar un error inmediato y claro.
- `dificultad` solo admite `facil`, `media`, `dificil` o vacío/null ("sin clasificar").
- La categoría de una pregunta se indica por `categoriaId` (si ya existe y se conoce el id) o por
  `categoriaNombre` (se reutiliza si ya existe una con ese nombre, o se crea). `categoriaId` tiene
  prioridad si se indican ambos.
- La API no tiene PATCH parcial: `editar_pregunta` reemplaza la pregunta entera. Para cambios puntuales
  usa las tools de conveniencia (`editar_dificultad_pregunta`, `anadir_respuesta`,
  `marcar_respuesta_correcta`), que internamente hacen *read-modify-write* (leen la pregunta actual,
  cambian solo el campo pedido, y guardan) preservando el resto de campos.

## Tools disponibles

### Autenticación

| Tool | Descripción |
|---|---|
| `login` | `username`, `password` → inicia sesión y guarda el token. |
| `whoami` | Datos del usuario autenticado (id, username, email, rol). |

### Categorías

| Tool | Descripción |
|---|---|
| `listar_categorias` | Categorías del usuario con el número de preguntas de cada una. |
| `crear_categoria` | `nombre` → crea una categoría nueva. |
| `renombrar_categoria` | `id`, `nombre` → renombra una categoría existente. |
| `eliminar_categoria` | `id` → borra la categoría (sus preguntas quedan sin categoría). |

### Preguntas

| Tool | Descripción |
|---|---|
| `listar_preguntas` | Filtros opcionales: `q`, `categoriaId`, `sinCategoria`, `dificultad`, `page`, `pageSize`. |
| `obtener_pregunta` | `id` → detalle completo de una pregunta. |
| `crear_pregunta` | `enunciado`, `respuestas` (lista de `{texto, esCorrecta}`), y opcionalmente `dificultad`, `categoriaId`/`categoriaNombre`, `imagenUrl`. |
| `editar_pregunta` | Igual que `crear_pregunta` pero reemplaza una pregunta existente por `id`. |
| `eliminar_pregunta` | `id` → borra la pregunta. |
| `asignar_categoria_preguntas` | `preguntaIds` (lista), `categoriaId` (o null) → mueve varias preguntas de una vez. |
| `editar_dificultad_pregunta` | `id`, `dificultad` → cambia solo la dificultad. |
| `anadir_respuesta` | `id`, `texto`, `esCorrecta` → añade una respuesta conservando las existentes. |
| `marcar_respuesta_correcta` | `id`, y `respuestaIndex` (0-based) o `respuestaTexto` → marca esa respuesta como correcta y desmarca el resto. |

### Ejemplo: crear una pregunta con una categoría nueva

```json
{
  "name": "crear_pregunta",
  "arguments": {
    "enunciado": "¿Capital de Francia?",
    "respuestas": [
      { "texto": "París", "esCorrecta": true },
      { "texto": "Londres", "esCorrecta": false },
      { "texto": "Madrid", "esCorrecta": false }
    ],
    "dificultad": "facil",
    "categoriaNombre": "Geografía"
  }
}
```

## Desarrollo

```bash
dotnet build TriviUpMcp
dotnet run --project TriviUpMcp
```

Para probarlo manualmente sin un cliente MCP completo, el
[MCP Inspector](https://github.com/modelcontextprotocol/inspector) oficial funciona contra cualquier
servidor stdio:

```bash
npx @modelcontextprotocol/inspector dotnet run --project TriviUpMcp
```
