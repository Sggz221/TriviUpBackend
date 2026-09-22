using System.ComponentModel;
using ModelContextProtocol.Server;
using TriviUpMcp.Client;

namespace TriviUpMcp.Tools;

[McpServerToolType]
public class CategoriaTools(TriviUpApiClient client)
{
    [McpServerTool(Name = "listar_categorias", ReadOnly = true)]
    [Description("Lista las categorías del banco personal de preguntas del usuario autenticado, con el número de preguntas de cada una.")]
    public Task<string> ListarCategorias(CancellationToken ct)
    {
        return ToolExecution.RunAsync(() => client.ListCategoriasAsync(ct));
    }

    [McpServerTool(Name = "crear_categoria")]
    [Description("Crea una nueva categoría en el banco de preguntas del usuario autenticado.")]
    public Task<string> CrearCategoria(
        [Description("Nombre de la categoría (máx. 50 caracteres)")] string nombre,
        CancellationToken ct)
    {
        return ToolExecution.RunAsync(() => client.CreateCategoriaAsync(nombre, ct));
    }

    [McpServerTool(Name = "renombrar_categoria")]
    [Description("Cambia el nombre de una categoría existente del usuario autenticado.")]
    public Task<string> RenombrarCategoria(
        [Description("Id de la categoría a renombrar")] long id,
        [Description("Nuevo nombre de la categoría (máx. 50 caracteres)")] string nombre,
        CancellationToken ct)
    {
        return ToolExecution.RunAsync(() => client.RenameCategoriaAsync(id, nombre, ct));
    }

    [McpServerTool(Name = "eliminar_categoria", Destructive = true)]
    [Description("Borra una categoría del banco de preguntas. Las preguntas que la usaban se conservan y quedan sin categoría.")]
    public Task<string> EliminarCategoria(
        [Description("Id de la categoría a eliminar")] long id,
        CancellationToken ct)
    {
        return ToolExecution.RunAsync(async () =>
        {
            await client.DeleteCategoriaAsync(id, ct);
            return new { message = $"Categoría {id} eliminada." };
        });
    }
}
