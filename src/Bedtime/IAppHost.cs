namespace Bedtime;

/// <summary>What the panel's view model needs from the app.</summary>
internal interface IAppHost
{
    /// <summary>Saves soon and re-plans now.</summary>
    void SettingsChanged();

    void SendTestReminder();

    void Quit();
}
