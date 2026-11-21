using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class FirstRunWindow : Window
{
    public AppSettings EditedSettings { get; }

    public FirstRunWindow()
    {
        InitializeComponent();
        EditedSettings = App.Current.Settings.Clone();
        SourceLangCombo.ItemsSource = Languages.SourceChoices;
        SourceLangCombo.SelectedItem = Languages.SourceChoices.FirstOrDefault(l => string.Equals(l.Code, EditedSettings.SourceLanguage, StringComparison.OrdinalIgnoreCase)) ?? Languages.AutoInfo;
        TargetLangCombo.ItemsSource = Languages.All;
        TargetLangCombo.SelectedItem = Languages.Resolve(string.IsNullOrEmpty(EditedSettings.TargetLanguage) ? Languages.DefaultTargetForCurrentSystem() : EditedSettings.TargetLanguage);
        HotkeyBox.Text = EditedSettings.Hotkey;
        StartupCheck.IsChecked = EditedSettings.StartWithWindows;
        ModelStatusText.Text = App.Current.Translations.OfflineAvailable
            ? "离线模型已就绪，断网也能使用。"
            : "未检测到离线模型。可先使用在线翻译，或在设置 → 识别与模型中下载语言包。";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(HotkeyBox.Text, out ParsedHotkey _, out var error))
        {
            ModelStatusText.Text = "热键无效：" + error;
            return;
        }

        EditedSettings.SourceLanguage = (SourceLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Auto;
        EditedSettings.TargetLanguage = (TargetLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Chinese.Code;
        EditedSettings.Hotkey = HotkeyBox.Text.Trim();
        EditedSettings.StartWithWindows = StartupCheck.IsChecked == true;
        EditedSettings.FirstRunCompleted = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        EditedSettings.FirstRunCompleted = true;
        DialogResult = false;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            return;
        }

        e.Handled = true;
        if (!HotkeyGesture.IsModifierKey(e.Key) && e.Key != Key.None)
        {
            HotkeyBox.Text = HotkeyGesture.Format(e.Key, Keyboard.Modifiers);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
