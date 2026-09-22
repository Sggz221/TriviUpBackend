# TriviUpMcpServer

La misma funcionalidad que [`TriviUpMcp`](../TriviUpMcp/README.md) (login, banco de preguntas,
categorías, cuestionarios — ver esa página para la tabla completa de tools), pero servida por
**HTTP** en vez de stdio, para que cualquiera con una cuenta de TriviUp pueda añadirla a su cliente
de Claude como conector remoto pegando una URL, sin instalar ni clonar nada.

Las tools en sí viven en `TriviUpMcp.Core` (librería compartida); este proyecto solo monta el
transporte HTTP ([Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http))
encima con ASP.NET Core.

## Por qué es un proyecto aparte

Un servidor MCP por HTTP es compartido por **todo el mundo que lo use a la vez**, a diferencia de
`TriviUpMcp` (stdio), donde cada usuario lanza su propio proceso. Eso tiene una implicación de
seguridad importante: **el token de sesión de cada usuario tiene que quedar aislado del de los
demás**. Este proyecto resuelve eso con `SessionAuthStore` (en `TriviUpMcp.Core/Auth`), un
diccionario en memoria indexado por el id de sesión MCP (`Mcp-Session-Id`, gestionado por el SDK en
modo `StatefulForInitializeClients`) — cada sesión solo puede leer/escribir su propia entrada.
Verificado con dos sesiones concurrentes logueadas con usuarios distintos: cada `whoami` devuelve
solo el usuario de su propia sesión.

**Importante si tocas este código:** no confíes en `AddScoped<T>()` de ASP.NET Core para guardar
estado "por sesión MCP" — cada llamada HTTP dentro de una misma sesión Streamable HTTP crea su
propio scope de request nuevo, así que un servicio Scoped normal NO persiste entre llamadas aunque
el `Mcp-Session-Id` sea el mismo. Por eso `AuthSessionState` es una fachada fina sobre el singleton
`SessionAuthStore`, no un simple `AddScoped<AuthSessionState>()` como en el host stdio.

## Configuración

| Variable | Descripción |
|---|---|
| `TRIVIUP_API_URL` | Backend contra el que trabaja. Por defecto `https://triviup-backend-production.up.railway.app` (a diferencia del host stdio, aquí el valor por defecto ya es producción, pensado para que cualquiera lo use sin configurar nada). |

## Endpoints

- `POST/GET /mcp` — Streamable HTTP transport (lo que registra el cliente MCP).
- `GET /health` — healthcheck simple para Railway.

## Cómo lo añade un usuario a su Claude

**claude.ai** (conector remoto): Settings → Connectors → Add custom connector → pega la URL
`https://<dominio-del-servicio>/mcp`.

**Claude Code**:

```bash
claude mcp add --transport http triviup-banco-preguntas https://<dominio-del-servicio>/mcp
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "triviup-banco-preguntas": {
      "url": "https://<dominio-del-servicio>/mcp"
    }
  }
}
```

En cualquier caso, el primer paso dentro de la conversación es llamar a la tool `login` con las
credenciales de TriviUp de esa persona — nadie ve ni puede usar las preguntas de otro usuario.

## Desarrollo local

```bash
dotnet run --project TriviUpMcpServer
```

Por defecto escucha en el puerto que indique `ASPNETCORE_URLS`/Kestrel (5000 en dev sin configurar).
Para probarlo contra el backend local en vez de producción:

```bash
TRIVIUP_API_URL=http://localhost:5164 dotnet run --project TriviUpMcpServer
```

## Despliegue (Railway)

Este servicio necesita ver el proyecto hermano `TriviUpMcp.Core`, así que a diferencia de
`TriviUpBackend` (cuyo root directory en Railway es su propia carpeta de proyecto), el **root
directory de este servicio debe ser la raíz del repo** (`TriviUpBackend/`), no `TriviUpMcpServer/`.
El `Dockerfile` de este proyecto ya asume ese contexto (copia `TriviUpMcp.Core/` y
`TriviUpMcpServer/` con rutas relativas a la raíz).

1. Nuevo servicio en el proyecto de Railway `TriviUp`, mismo repo (`Sggz221/TriviUpBackend`).
2. Root directory: `/` (raíz del repo). Dockerfile path: `TriviUpMcpServer/Dockerfile`.
3. Variable de entorno `TRIVIUP_API_URL` → la URL del servicio `TriviUp-Backend` (normalmente no
   hace falta tocarla: el valor por defecto del código ya apunta ahí).
4. Generar dominio público para el servicio. Healthcheck: `GET /health`.
5. Probar: añadir `https://<dominio>/mcp` como conector y hacer `login` con un usuario de prueba.
