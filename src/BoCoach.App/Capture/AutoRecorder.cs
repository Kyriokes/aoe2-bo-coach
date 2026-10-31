namespace BoCoach.App.Capture;

/// <summary>Grabar y cortar (lo que hace el botón "Grabar"; lo implementa <see cref="CaptureToolsViewModel"/>).</summary>
public interface IRecordingControl
{
    bool IsRecording { get; }

    void StartRecording();

    void StopRecording();
}

/// <summary>
/// "Grabar cada partida sola" (modo desarrollador): arranca cuando empieza una partida (<c>CoachFrame.InGame</c> pasa a
/// true) y corta cuando termina o se detiene el coach. Solo corta lo que arrancó: una grabación hecha a mano no se
/// toca, y si el jugador corta la suya a mano no vuelve a arrancar hasta la partida siguiente.
/// </summary>
public sealed class AutoRecorder(IRecordingControl recording, Func<bool> enabled)
{
    private bool _inGame;
    private bool _mine;

    public void OnFrame(bool inGame)
    {
        if (inGame && !_inGame)
        {
            _inGame = true;
            if (enabled() && !recording.IsRecording)
            {
                recording.StartRecording();
                _mine = true;
            }
        }
        else if (inGame && _mine && !recording.IsRecording)
            _mine = false; // la cortó a mano: si graba de nuevo, es suya
        else if (!inGame && _inGame)
            End();
    }

    public void OnCoachStopped() => End();

    private void End()
    {
        _inGame = false;
        if (_mine && recording.IsRecording)
            recording.StopRecording();
        _mine = false;
    }
}
