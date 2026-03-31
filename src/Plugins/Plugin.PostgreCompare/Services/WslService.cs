using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// WSL ディストリビューション情報取得サービス（簡易版）。
/// ホスト環境依存のため、ここでは空リストまたは固定値のみ返す。
/// </summary>
public class WslService
{
    public Task<IReadOnlyList<string>> GetWslDistributionsAsync()
    {
        // 必要に応じて実装を拡張（例：Process.Start で wsl.exe -l -q を叩く）
        IReadOnlyList<string> result = new List<string>();
        return Task.FromResult(result);
    }
}

