# 🎯 TriviUp

> Plataforma de concursos de trivia multiplayer en tiempo real

---

Desafía a tus amigos, pon a prueba tus conocimientos y diviértete con emocionantes partidas donde cada turno es una pregunta con temporizador y **puntuación por velocidad de respuesta**.

---

## ✨ Características

| | |
|---|---|
| 🚀 **Rápido y Fácil** | Crea salas en segundos y empieza a jugar |
| 🎨 **Personalizable** | Diseña tus propias preguntas y categorías |
| 🏆 **Competitivo** | Sube en el ranking y demuestra tu conocimiento |
| 👥 **Multiplayer** | Juega con amigos en tiempo real |
| 🔒 **Seguro** | Autenticación con JWT y Google OAuth |

---

## 🛠️ Tecnologías

### Backend
- **.NET 9** · ASP.NET Core · Entity Framework Core
- **PostgreSQL** · **Redis** (estado de partidas + SignalR backplane) · SignalR · JWT · BCrypt

### Frontend
- **Angular 21** · TypeScript · Tailwind CSS
- **DaisyUI 5** · SignalR · RxJS · Vitest

---

## 🚀 Despliegue (Railway)

### Variables de entorno requeridas

| Variable | Descripción |
|---|---|
| `DATABASE_URL` | Connection string PostgreSQL (plugin Railway, host privado) |
| `DATABASE_PUBLIC_URL` | **Recomendada en Railway** — URL pública del TCP proxy (`proxy.rlwy.net`). El backend la usa automáticamente si `DATABASE_URL` apunta a `*.railway.internal` |
| `USE_DATABASE_PUBLIC_URL` | `true` para forzar siempre la URL pública |
| `PREFER_PRIVATE_DATABASE` | `true` para forzar el host privado (requiere private networking / IPv6) |
| `REDIS_URL` | Connection string Redis (`redis://` / `rediss://`) — **obligatoria en producción** |
| `Jwt__Key` | Clave de firma JWT |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | OAuth Google |

### Postgres en Railway (crashes por DNS)

Si ves `SocketException: Name or service not known` con un host `*.railway.internal`, el DNS privado de Railway no está resolviendo (red privada / IPv6). Solución rápida:

1. En el servicio **backend**, añade la variable:
   `DATABASE_PUBLIC_URL=${{Postgres.DATABASE_PUBLIC_URL}}`
   (sustituye `Postgres` por el nombre real del servicio Postgres en tu proyecto).
2. Redeploy. El código prioriza esa URL cuando el host es interno.
3. Opcional: `USE_DATABASE_PUBLIC_URL=true` para forzarla siempre.

También se aceptan URIs `postgres://` y `postgresql://` (antes solo se convertía `postgresql://`).

### Redis

El estado en vivo de las salas (`GameService`) y el backplane de SignalR usan Redis. Sin `REDIS_URL` en producción la API no arranca.

En desarrollo local, si no hay Redis, el backend cae a un almacén in-memory y SignalR sin backplane (solo 1 instancia).

1. Añade el plugin **Redis** al servicio backend en Railway.
2. Verifica que `REDIS_URL` esté inyectada.
3. Health check: `GET /health` (incluye ping a Redis cuando está configurado).
4. Puedes escalar a **varias réplicas**; el estado y los grupos SignalR se comparten vía Redis.

---

## 🔗 Enlaces

**Aplicación:** https://triviup.up.railway.app/

**Repositorios:**
- Backend → https://github.com/charlieecy/TriviUpBackend
- Frontend → https://github.com/Sggz221/TriviUpFrontend

---

Hecho con ❤️ por [Samuel Gómez](https://github.com/Sggz221) y [Carlos Cortés](https://github.com/charlieecy)
