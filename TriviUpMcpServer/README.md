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

## OAuth (carpeta `OAuth/`)

claude.ai exige que un "custom connector" hable OAuth para registrarse y autenticar (metadata +
registro dinámico de cliente + Authorization Code con PKCE) — sin esto, el botón "Add custom
connector" falla con un error genérico de "couldn't register with the sign-in service" antes
siquiera de intentar conectar. Este proyecto implementa el mínimo Authorization Server necesario
para eso, haciendo de puente hacia el login/JWT que ya tiene TriviUp:

| Endpoint | Qué hace |
|---|---|
| `GET /.well-known/oauth-protected-resource` | RFC 9728: le dice al cliente dónde está el Authorization Server. |
| `GET /.well-known/oauth-authorization-server` | RFC 8414: endpoints, grants y métodos soportados. |
| `POST /register` | RFC 7591: registro dinámico de cliente (público, sin `client_secret`). |
| `GET /authorize` | Formulario HTML de login (usuario/contraseña de TriviUp). |
| `POST /authorize` | Valida contra `POST /Auth/signin` de TriviUp; si es correcto, redirige con un `code`. |
| `POST /token` | Canjea `code` (+ PKCE `code_verifier`) o `refresh_token` por un access token. |

**Decisión clave que simplifica todo lo demás:** el `access_token` que emitimos **es literalmente
el JWT de TriviUp** — no hay un token opaco propio que mantener sincronizado. Cada request a `/mcp`
se valida llamando a `GET /Users/me` de TriviUp con ese mismo Bearer
(`TriviUpBearerAuthenticationHandler`); si es válido, esa llamada también siembra
`SessionAuthStore` para la sesión MCP actual, así que las tools ven al usuario ya autenticado sin
necesidad de llamar a `login` (aunque `login` se deja disponible por si alguien quiere cambiar de
cuenta a mitad de conversación). El `refresh_token` grant llama a `POST /Auth/refresh` de TriviUp;
si el JWT ya caducó del todo, la renovación falla y el cliente tiene que volver a pasar por
`/authorize` — es una limitación aceptada, no un bug.

`/mcp` requiere este Bearer token (`RequireAuthorization()`); sin él, responde `401` con
`WWW-Authenticate: Bearer resource_metadata="…"` para que el cliente arranque el flujo OAuth solo.

**Nunca se guarda la contraseña de TriviUp en ningún sitio** — `/authorize` solo la reenvía, una
vez, a `POST /Auth/signin`.

## Configuración

| Variable | Descripción |
|---|---|
| `TRIVIUP_API_URL` | Backend contra el que trabaja. Por defecto `https://triviup-backend-production.up.railway.app` (a diferencia del host stdio, aquí el valor por defecto ya es producción, pensado para que cualquiera lo use sin configurar nada). |

## Endpoints

- `POST/GET /mcp` — Streamable HTTP transport (lo que registra el cliente MCP). Requiere OAuth.
- `GET /health` — healthcheck simple para Railway (sin auth).
- Los de OAuth, ver arriba.

## Cómo lo añade un usuario a su Claude

**claude.ai** (conector remoto): Settings → Connectors → Add custom connector → pega la URL
`https://<dominio-del-servicio>/mcp`. claude.ai hace todo el flujo OAuth solo: te enseña el
formulario de login de TriviUp en una ventana emergente y, tras autenticarte, queda conectado.

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

En los tres casos, al conectar se abre el login de TriviUp (usuario/contraseña) una sola vez; a
partir de ahí, cada persona ya está autenticada como su propia cuenta dentro de la conversación —
no hace falta llamar a la tool `login`, y nadie ve ni puede usar las preguntas de otro usuario.

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
