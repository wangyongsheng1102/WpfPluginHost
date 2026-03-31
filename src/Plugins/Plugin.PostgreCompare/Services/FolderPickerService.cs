using System;
using System.IO;
using Microsoft.Win32;

namespace Plugin.PostgreCompare.Services;

public static class FolderPickerService
{
    public static string? PickFolder(string description)
    {
        try
        {
            // SampleA と同様に OpenFileDialog ベースでシンプルに実装する。
            // フォルダを直接選べないため、任意ファイルを選択してその親フォルダを採用する。
            var dlg = new OpenFileDialog
            {
                Title = description,
                CheckFileExists = false,
                ValidateNames = false,
                FileName = "フォルダを選択"
            };

            if (dlg.ShowDialog() != true)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(dlg.FileName))
            {
                var candidateDir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrWhiteSpace(candidateDir) && Directory.Exists(candidateDir))
                {
                    return candidateDir;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

