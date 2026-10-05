namespace TriviUpBackend.Game.Services;

/// <summary>
/// Reparto equitativo de turnos: todos los jugadores responden el mismo número de preguntas.
/// Las preguntas de pulsador no cuentan, porque las responde quien pulsa primero.
/// </summary>
public static class FairTurnPlanner
{
    /// <summary>
    /// Número de preguntas por turno que se pueden jugar: el mayor múltiplo de <paramref name="playerCount"/>
    /// que no supera <paramref name="turnQuestionCount"/>. Con más jugadores que preguntas se mantienen
    /// todas, porque recortar dejaría la partida vacía.
    /// </summary>
    public static int FairTurnQuestionCount(int turnQuestionCount, int playerCount)
    {
        if (playerCount <= 1 || turnQuestionCount < playerCount) return turnQuestionCount;
        return turnQuestionCount - turnQuestionCount % playerCount;
    }

    /// <summary>
    /// Quita las últimas preguntas por turno que sobran para que el reparto sea equitativo.
    /// Mantiene el orden y las preguntas de pulsador.
    /// </summary>
    public static List<T> TrimToFair<T>(List<T> questions, int playerCount, Func<T, bool> isBuzzer)
    {
        var turnCount = questions.Count(q => !isBuzzer(q));
        var toRemove = turnCount - FairTurnQuestionCount(turnCount, playerCount);
        if (toRemove == 0) return questions;

        var result = new List<T>(questions);
        for (var i = result.Count - 1; i >= 0 && toRemove > 0; i--)
        {
            if (isBuzzer(result[i])) continue;
            result.RemoveAt(i);
            toRemove--;
        }
        return result;
    }
}
