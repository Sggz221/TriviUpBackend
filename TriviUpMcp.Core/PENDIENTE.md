# MCP: cambios pendientes

El MCP (`TriviUpMcp.Core`, que usan `TriviUpMcp` y `TriviUpMcpServer`) se quedó atrás respecto a la API en
tres cambios recientes de los cuestionarios. Aquí está lo que hay que hacer para ponerlo al día.

Contratos de referencia en el backend:
- `TriviUpBackend/Cuestionarios/DTOs/CreateQuizRequest.cs`, `UpdateQuizRequest.cs` y `QuizResponse.cs`.
- `TriviUpBackend/Cuestionarios/Models/TiposPregunta.cs` y `FasePool.cs`.

## ⚠️ Urgente: `editar_cuestionario` borra datos

`editar_cuestionario` reemplaza el cuestionario entero (`PUT`). Ahora mismo su request no lleva ni `tipo` ni
`pools`, así que **cualquier edición hecha desde el MCP**:

- **convierte en normales todas las preguntas de pulsador** (el backend normaliza un `tipo` ausente a `normal`);
- **elimina todas las fases con pool de preguntas** (un `pools` ausente es una lista vacía).

Además, si el cuestionario tiene pools, los números de fase de las preguntas fijas dejan huecos (p. ej. 1 y 3). Al
publicar sin la fase 2, la validación falla con *"Las fases deben ser consecutivas"*. Si se guarda como borrador,
se guarda con los huecos.

Mientras no se arregle, conviene que la descripción de la tool avise de esto, o que la tool se niegue a editar
cuestionarios con pools o preguntas de pulsador.

## 1. Tipo de pregunta (`normal` / `pulsador`)

Las preguntas tienen ahora `tipo`: `"normal"` (por defecto) o `"pulsador"` (nadie tiene turno y responde el primer
equipo en pulsar desde su móvil). Sustituye a la antigua `faseDinamica`, que nunca llegó al MCP.

- `Client/TriviUpModels.cs`:
  - añadir `[property: JsonPropertyName("tipo")] string? Tipo = null` a `PreguntaCuestionarioInput`;
  - añadir `string Tipo` a `PreguntaCuestionarioResponse`.
- `Tools/CuestionarioTools.cs`: mencionar `tipo` en la descripción del parámetro `preguntas` de
  `crear_cuestionario` y `editar_cuestionario`, explicando qué hace «pulsador».
- Valores válidos: `normal`, `pulsador`. Al publicar, otro valor da error de validación; en borrador se guarda como
  `normal`.

## 2. Pool de preguntas por fase (TRI-25)

Una fase puede ser un **pool**: no tiene preguntas propias y en cada partida se sortean `cantidad` preguntas del banco
del autor. Las preguntas salen elegidas a mano o por filtros. Viaja en `pools` (lista) del request y de la respuesta.

Forma de cada pool (JSON, camelCase):

```json
{
  "faseNumero": 2,
  "faseNombre": "Historia",
  "faseColor": "#ff8800",
  "cantidad": 5,
  "origen": "filtros",
  "preguntas": [],
  "categoriaId": 3,
  "categoriaNombre": "Historia",
  "dificultad": "facil"
}
```

- `origen: "manual"`: `preguntas` es `[{ "id": <id del banco>, "enunciado": "<solo para mostrar>" }]`. Los ids quedan
  enlazados al banco. `categoriaId` y `dificultad` se ignoran.
- `origen: "filtros"`: `categoriaId` y `dificultad` son opcionales (null = cualquiera). `preguntas` se ignora.

Reglas al publicar (en borrador se normaliza sin fallar):
- `cantidad` va de 1 a 50.
- En los manuales, `cantidad` no puede ser mayor que el número de preguntas elegidas.
- Las fases de preguntas y las de pool juntas van de 1 a n sin huecos. Una fase no puede tener pool y preguntas a la
  vez.
- Un cuestionario puede tener solo pools, sin preguntas fijas.

Cambios:
- `Client/TriviUpModels.cs`:
  - nuevos records `FasePoolInput` / `FasePoolPreguntaInput` (y sus equivalentes de respuesta, o los mismos);
  - añadir `List<FasePoolInput>? Pools` a `CreateCuestionarioRequest` y `UpdateCuestionarioRequest`;
  - añadir `List<...> Pools` a `CuestionarioResponse`.
- `Tools/CuestionarioTools.cs`:
  - parámetro opcional `pools` en `crear_cuestionario` y `editar_cuestionario`, con su descripción;
  - en `editar_cuestionario` es imprescindible poder reenviarlo; ver la sección urgente.
  - `obtener_cuestionario` / `listar_mis_cuestionarios`: que la salida muestre los pools, para que el agente los
    vea antes de editar.
- Descripciones de las tools: explicar que la numeración de fases es común a preguntas y pools.

### Tools que facilitarían el uso (opcional)

- `configurar_pool_fase(cuestionarioId, faseNumero, cantidad, origen, preguntaIds?, categoriaId?, dificultad?)`:
  hace un GET, modifica solo ese pool y hace un PUT del cuestionario completo (preguntas incluidas). Así se evita que el agente
  tenga que reconstruir todo el cuestionario a mano.
- `quitar_pool_fase(cuestionarioId, faseNumero)`.
- Al crear un pool por filtros, avisar si ahora mismo el banco tiene menos preguntas de las pedidas. Usar
  `GET api/banco-preguntas?categoriaId=&dificultad=&pageSize=1` y leer `totalCount`. No es un error: la partida juega
  las que haya.

## 3. Efectos de las tools del banco sobre los pools

- `eliminar_pregunta` (banco): si la pregunta estaba elegida a mano en algún pool, sigue apareciendo en él (con el
  enunciado guardado) pero en la partida se ignora. Se podría avisar en la respuesta de la tool, o limpiar los pools
  afectados.
- `editar_pregunta` (banco): en las partidas se usa ya la versión nueva. El `enunciado` guardado en el pool solo se
  actualiza cuando el autor vuelve a guardar el cuestionario.

## Checklist

- [ ] `editar_cuestionario` conserva `tipo` y `pools` (o se niega a editar si no los recibe).
- [ ] `tipo` en input y respuesta de preguntas.
- [ ] `pools` en crear / editar / obtener.
- [ ] Descripciones de las tools actualizadas.
- [ ] (Opcional) `configurar_pool_fase` / `quitar_pool_fase`.
- [ ] Actualizar `TriviUpMcp/README.md` y `TriviUpMcpServer/README.md` con lo nuevo.
