using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Abstractions;
using Plugin.ReviewCheck.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Plugin.ReviewCheck.ViewModels;

public sealed partial class ReviewCheckViewModel : ObservableObject
{
    private static readonly string[] _inputTypeOptions = ["画面", "API"];
    private static readonly string[] _systemOptions = ["EnabilityCis", "EnabilityOrder", "EnabilityPortal", "EnabilityPortal2"];
    private static readonly string[] _objectOptions = ["API", "バッチ", "マルチ", "課題対応", "画面", "環境構築", "共通部品", "結合テスト", "差分結合", "差分取込", "性能テスト"];

    private readonly IPluginContext? _context;
    private readonly ReviewCheckOrchestrator _orchestrator;
    private CancellationTokenSource? _cts;
    private ReviewCheckRequest? _lastRequest;

    public ObservableCollection<CheckResultItem> Results { get; } = new();

    [ObservableProperty]
    private string _svnRootPath = string.Empty;

    [ObservableProperty]
    private string _wbsExcelPath = string.Empty;

    [ObservableProperty]
    private string _functionId = string.Empty;

    [ObservableProperty]
    private string _systemName = "EnabilityCis";

    [ObservableProperty]
    private string _objectName = "画面";

    [ObservableProperty]
    private string _inputType = "画面";

    [ObservableProperty]
    private string _summaryText = "未実行";

    [ObservableProperty]
    private bool _isRunning;

    public bool CanRun => !IsRunning && Directory.Exists(SvnRootPath);
    public bool HasResults => Results.Count > 0;
    public IReadOnlyList<string> InputTypeOptions => _inputTypeOptions;
    public IReadOnlyList<string> SystemOptions => _systemOptions;
    public IReadOnlyList<string> ObjectOptions => _objectOptions;

    public ReviewCheckViewModel(IPluginContext? context)
    {
        _context = context;
        _orchestrator = new ReviewCheckOrchestrator(context);

        LoadSettings();
        Results.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasResults));
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsRunning) or nameof(SvnRootPath))
            {
                OnPropertyChanged(nameof(CanRun));
            }
        };
    }

    [RelayCommand]
    private void BrowseSvnRoot()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "SVN 成果物ルートフォルダを選択してください。"
        };

        var result = dialog.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            SvnRootPath = dialog.SelectedPath;
            SaveSettings();
        }
    }

    [RelayCommand]
    private void BrowseWbsExcel()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "WBS Excel を選択…",
            Filter = "Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            WbsExcelPath = dialog.FileName;
            SaveSettings();
        }
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        SummaryText = "クリアしました";
        _context?.ClearStatus();
    }

    [RelayCommand]
    private async Task RunChecksAsync()
    {
        if (!Directory.Exists(SvnRootPath))
        {
            System.Windows.MessageBox.Show("成果物ルート（SVN）のパスが無効です。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsRunning = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Results.Clear();
        SummaryText = "実行中…";
        SaveSettings();

        try
        {
            var request = new ReviewCheckRequest
            {
                SvnRootPath = SvnRootPath,
                WbsExcelPath = WbsExcelPath,
                FunctionId = FunctionId,
                InputType = InputType,
                SystemName = SystemName,
                ObjectName = ObjectName
            };
            _lastRequest = request;

            var items = await _orchestrator.RunAsync(request, _cts.Token).ConfigureAwait(true);
            foreach (var item in items)
            {
                Results.Add(item);
            }

            SummaryText = $"完了：{Results.Count} 件";
            _context?.ReportSuccess("整合性チェックが完了しました。");
        }
        catch (OperationCanceledException)
        {
            SummaryText = "キャンセルしました";
            _context?.ReportWarning("処理をキャンセルしました。");
        }
        catch (Exception ex)
        {
            SummaryText = "失敗";
            _context?.ReportError($"失敗: {ex.Message}");
            System.Windows.MessageBox.Show(ex.ToString(), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void ExportJson()
    {
        if (!HasResults)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "結果を JSON で保存…",
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"review-check-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var payload = new
            {
                generatedAt = DateTimeOffset.Now,
                request = _lastRequest,
                results = Results
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(dialog.FileName, json);
            _context?.ReportSuccess($"JSON を保存しました: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _context?.ReportError($"JSON 保存に失敗: {ex.Message}");
            System.Windows.MessageBox.Show(ex.ToString(), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings()
    {
        if (_context is null)
        {
            return;
        }

        try
        {
            var json = _context.GetPluginSetting("reviewCheck");
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<ReviewCheckSettings>(json);
            if (settings is null)
            {
                return;
            }

            SvnRootPath = settings.SvnRootPath ?? string.Empty;
            WbsExcelPath = settings.WbsExcelPath ?? string.Empty;
            FunctionId = settings.FunctionId ?? string.Empty;
            SystemName = string.IsNullOrWhiteSpace(settings.SystemName) ? "EnabilityCis" : settings.SystemName;
            ObjectName = string.IsNullOrWhiteSpace(settings.ObjectName) ? "画面" : settings.ObjectName;
            InputType = string.IsNullOrWhiteSpace(settings.InputType) ? "画面" : settings.InputType;
        }
        catch
        {
            // 設定破損時は UI を止めない。
        }
    }

    private void SaveSettings()
    {
        if (_context is null)
        {
            return;
        }

        try
        {
            var settings = new ReviewCheckSettings
            {
                SvnRootPath = SvnRootPath,
                WbsExcelPath = WbsExcelPath,
                FunctionId = FunctionId,
                SystemName = SystemName,
                ObjectName = ObjectName,
                InputType = InputType
            };
            _context.SavePluginSetting("reviewCheck", JsonSerializer.Serialize(settings));
        }
        catch
        {
            // 保存失敗時も UI は継続。
        }
    }
}

internal sealed class ReviewCheckSettings
{
    public string? SvnRootPath { get; set; }
    public string? WbsExcelPath { get; set; }
    public string? FunctionId { get; set; }
    public string? SystemName { get; set; }
    public string? ObjectName { get; set; }
    public string? InputType { get; set; }
}
