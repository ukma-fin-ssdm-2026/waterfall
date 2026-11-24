namespace waterfall.UI.Views;

/// <summary>
/// Lifecycle contract implemented by pages hosted inside <see cref="DesktopHostPage"/>,
/// <see cref="PhoneHostPage"/>, or <see cref="PhoneSectionHostPage"/>.
/// </summary>
internal interface IHostedPage
{
    void OnHostActivated() { }
    void OnHostDeactivated() { }
    bool OnHostBackPressed() => false;
}
