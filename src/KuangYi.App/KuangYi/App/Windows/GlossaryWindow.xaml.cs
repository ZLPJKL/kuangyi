using System;
using System.Windows;
using System.Windows.Input;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class GlossaryWindow : Window
{
    private readonly GlossaryService _service;

    public GlossaryWindow()
    {
        InitializeComponent();
        _service = App.Current.Translations.Glossary;
        EditorBox.Text = _service.ReadRaw();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.WriteRaw(EditorBox.Text);
            StatusText.Text = $"已保存，共 {_service.Count} 条术语；下一次翻译自动生效。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入术语表 CSV",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _service.ImportCsv(dialog.FileName);
            EditorBox.Text = _service.ReadRaw();
            StatusText.Text = $"已导入 CSV，共 {_service.Count} 条术语。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出术语表 CSV",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            FileName = "glossary.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _service.WriteRaw(EditorBox.Text);
            _service.ExportCsv(dialog.FileName);
            StatusText.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            StatusText.Text = "导出失败：" + ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
