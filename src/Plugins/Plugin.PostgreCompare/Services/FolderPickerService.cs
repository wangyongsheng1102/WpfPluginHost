using System;
using System.Reflection;

namespace Plugin.PostgreCompare.Services;

public static class FolderPickerService
{
    public static string? PickFolder(string description)
    {
        try
        {
            var dialogType = Type.GetType("System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms");
            if (dialogType == null)
            {
                return null;
            }

            var dialog = Activator.CreateInstance(dialogType);
            if (dialog == null)
            {
                return null;
            }

            using var disposable = dialog as IDisposable;

            dialogType.GetProperty("Description", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(dialog, description);

            var showDialog = dialogType.GetMethod("ShowDialog", Type.EmptyTypes);
            var result = showDialog?.Invoke(dialog, null);

            if (result == null || !string.Equals(result.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return dialogType.GetProperty("SelectedPath", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(dialog) as string;
        }
        catch
        {
            return null;
        }
    }
}

