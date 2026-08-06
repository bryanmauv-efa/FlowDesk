using TermServMultiScreen.Core;

namespace TermServMultiScreen.Services;

/// <summary>
/// Raccourcis clavier globaux pour déplacer la session Bureau à distance <b>sans la quitter</b> :
/// <list type="bullet">
///   <item>Ctrl + Alt + Maj + ← / → : écran précédent / suivant</item>
///   <item>Ctrl + Alt + Maj + 1…9 : écran choisi (rang de gauche à droite)</item>
/// </list>
///
/// Les raccourcis sont posés sur un fil dédié avec sa propre boucle de messages :
/// <c>RegisterHotKey(0, …)</c> adresse WM_HOTKEY au fil appelant, ce qui évite d'avoir à
/// détourner la fenêtre WinUI — un WndProc de remplacement suffirait à faire tomber l'interface.
///
/// Windows traite ses raccourcis avant de livrer la touche à la fenêtre active : la combinaison
/// n'est donc pas envoyée au serveur distant. Si une autre application a déjà réservé la même
/// combinaison, l'enregistrement échoue proprement et seule l'interface reste utilisable.
/// </summary>
public sealed class RdpHotkeyService : IDisposable
{
    private const uint Combination =
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;

    private const int IdPrevious = 9101;
    private const int IdNext = 9102;
    /// <summary>Identifiant de l'écran n : <c>IdScreenBase + n</c>.</summary>
    private const int IdScreenBase = 9110;
    private const int MaxScreens = 9;

    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _pump;
    private uint _pumpThreadId;
    private volatile bool _stopping;
    private volatile bool _registered;

    /// <summary>Écran demandé, de 1 à 9 (rang de gauche à droite).</summary>
    public event Action<int>? ScreenRequested;

    /// <summary>Écran voisin : -1 pour le précédent, +1 pour le suivant.</summary>
    public event Action<int>? StepRequested;

    /// <summary>Vrai si au moins une combinaison a pu être réservée.</summary>
    public bool Registered => _registered;

    public static string Description =>
        "Ctrl + Alt + Maj + ← / →  (écran précédent / suivant)     ·     Ctrl + Alt + Maj + 1…9  (écran choisi)";

    public void Start()
    {
        if (_pump is not null) return;

        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Raccourcis Bureau à distance"
        };
        _pump.Start();

        // Onze RegisterHotKey prennent moins d'une milliseconde : cette attente très courte permet
        // à l'interface d'annoncer tout de suite si les raccourcis sont actifs, sans jamais bloquer.
        _ready.Wait(TimeSpan.FromMilliseconds(500));
    }

    private void Pump()
    {
        _pumpThreadId = NativeMethods.GetCurrentThreadId();
        List<int> reserved = [];

        try
        {
            if (NativeMethods.RegisterHotKey(0, IdPrevious, Combination, NativeMethods.VK_LEFT))
                reserved.Add(IdPrevious);
            if (NativeMethods.RegisterHotKey(0, IdNext, Combination, NativeMethods.VK_RIGHT))
                reserved.Add(IdNext);

            for (int screen = 1; screen <= MaxScreens; screen++)
            {
                // Codes virtuels des chiffres de la rangée du haut : '1' vaut 0x31.
                uint key = (uint)('0' + screen);
                if (NativeMethods.RegisterHotKey(0, IdScreenBase + screen, Combination, key))
                    reserved.Add(IdScreenBase + screen);
            }

            _registered = reserved.Count > 0;
            Log.Write(_registered
                ? $"Raccourcis de déplacement actifs ({reserved.Count} combinaisons) : {Description}"
                : "Aucun raccourci de déplacement n'a pu être réservé (combinaisons déjà prises) — "
                  + "les boutons de la page Sessions restent utilisables.");
        }
        catch (Exception ex)
        {
            Log.Write($"Enregistrement des raccourcis de déplacement : {ex.Message}");
        }
        finally
        {
            _ready.Set();
        }

        try
        {
            while (!_stopping && NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.message == NativeMethods.WM_HOTKEY) Dispatch((int)message.wParam);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Boucle des raccourcis de déplacement : {ex.Message}");
        }
        finally
        {
            foreach (int id in reserved)
            {
                try { NativeMethods.UnregisterHotKey(0, id); }
                catch { /* le fil se termine : rien de plus à faire */ }
            }
            _registered = false;
        }
    }

    private void Dispatch(int id)
    {
        try
        {
            if (id == IdPrevious) StepRequested?.Invoke(-1);
            else if (id == IdNext) StepRequested?.Invoke(+1);
            else if (id > IdScreenBase && id <= IdScreenBase + MaxScreens) ScreenRequested?.Invoke(id - IdScreenBase);
        }
        catch (Exception ex)
        {
            Log.Write($"Traitement du raccourci {id} : {ex.Message}");
        }
    }

    public void Dispose()
    {
        _stopping = true;

        var pump = _pump;
        _pump = null;
        if (pump is not null)
        {
            try
            {
                // WM_QUIT sort de GetMessage : le fil libère alors ses combinaisons de lui-même.
                if (_pumpThreadId != 0)
                    NativeMethods.PostThreadMessage(_pumpThreadId, NativeMethods.WM_QUIT, (nuint)0, 0);
                pump.Join(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Log.Write($"Arrêt des raccourcis de déplacement : {ex.Message}");
            }
        }

        _ready.Dispose();
    }
}
