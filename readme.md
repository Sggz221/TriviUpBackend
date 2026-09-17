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
| `DATABASE_URL` | Connection string PostgreSQL (plugin Railway) |
| `REDIS_URL` | Connection string Redis (`redis://` / `rediss://`) — **obligatoria en producción** |
| `Jwt__Key` | Clave de firma JWT |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | OAuth Google |

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
