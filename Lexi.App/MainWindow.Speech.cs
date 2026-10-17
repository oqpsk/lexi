using System.Windows;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly SpeechPlayback speech = new(App.UserRoot);
    private int speechRequest;
    private string speechText = "";
    private void StopSpeech()
    {
        speechRequest++; speech.Stop(); speakingKey = null;
        SpeechButton.Content = Theme.Icon("SpeakerIcon"); SpeechButton.ToolTip = "播放合成发音";
        ShowReadAloud();
    }
    private async void Speech_Click(object sender, RoutedEventArgs e)
    {
        if (speakingKey == HeadingSpeech) { StopSpeech(); LookupStatus.Text = "发音已停止"; return; }
        await SpeakAsync(speechText, HeadingSpeech);
    }
    // key is the headword or the definition or example being read; only its controls show playback.
    private async Task SpeakAsync(string text, string key)
    {
        StopSpeech(); var request = speechRequest; speakingKey = key;
        if (key == HeadingSpeech) SpeechButton.Content = Theme.Icon("StopIcon");
        ShowReadAloud();
        try
        {
            await speech.PlayAsync(text, SpeechOptions.From(settings), settings.Online, message =>
            { if (request == speechRequest) { if (key == HeadingSpeech) SpeechButton.ToolTip = message + " · 点击停止"; LookupStatus.Text = message; } });
            if (request == speechRequest) LookupStatus.Text = "发音播放结束";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception)
        { if (request == speechRequest) LookupStatus.Text = ex.Message; }
        finally { if (request == speechRequest) StopSpeech(); }
    }
}
