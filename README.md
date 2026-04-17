# WPF プラグインシェル（.NET 8）

## プロジェクト構成

- `src/ShellApp`：メインシェル（左側の折りたたみメニュー＋中央の動的コンテンツ）
- `src/Plugin.Abstractions`：プラグイン契約 `IPluginModule` およびホスト通信用 `IPluginContext`
- `src/Plugins/Plugin.SampleA`：サンプルプラグイン A（Excel 書式設定）
- `src/Plugins/Plugin.SampleB`：サンプルプラグイン B（画像プレビュー）
- `src/Plugins/Plugin.PixelCompare`：画像比較プラグイン（Excel 内の画像を抽出・比較）
- `src/Plugins/Plugin.PostgreCompare`：データ比較プラグイン（DB・CSV の比較、インポート/エクスポート）
- `plugins`：実行時プラグインディレクトリ（ホットリロードの監視対象）

## 主要機能

- **動的プラグインロード**: アプリ起動中であっても `plugins` フォルダ内の DLL をスキャンし、自動的にメニューへ追加・更新します。
- **プラグインの分離**: 各プラグインは独自の `AssemblyLoadContext` で読み込まれ、依存関係の競合を最小限に抑えます。
- **シャドウコピー**: プラグイン DLL を一時ディレクトリにコピーして読み込むため、アプリ実行中でも DLL の上書き（再ビルド）が可能です。
- **ホスト通信 (IPluginContext)**: プラグインからホスト側のステータスバー、進捗表示、通知機能へ簡単にアクセスできます。
- **最適化された画像比較 (PixelCompare)**: 
  - Excel 内の図形（画像）を高速にインデックス化し、大量の画像でも瞬時に抽出します。
  - `ImageSharp` の低レベルアクセスにより、ピクセル単位の比較を高速に実行します。
  - レポート出力の並列処理、一時ファイルのリソース管理が最適化されています。

## 実行手順

1. Windows 環境で .NET 8 SDK を用意し、`WpfPluginHost.sln` を開く。
2. プラグインプロジェクトをビルドする。ビルド後、プラグイン DLL はリポジトリ直下の `plugins` に自動コピーされる。
3. `ShellApp` を起動すると、左メニューにプラグイン数に応じたエントリが表示される。
4. 実行中に `plugins` 内の DLL を置き換えると、ホットリロードで一覧が更新される。

## プラグインの追加手順

以下は、メインから動的に読み込めるプラグイン DLL を新規追加するための手順です。

1. `src/Plugins` 配下に WPF 対応のクラスライブラリを作成する（例：`Plugin.SampleC`）。
2. プラグインの `csproj` を編集する。
   - ターゲットフレームワークは `net8.0-windows`
   - `<UseWPF>true</UseWPF>` を有効にする
   - `src/Plugin.Abstractions/Plugin.Abstractions.csproj` を参照する
3. プラグイン内で `IPluginModule` を実装する。少なくとも次を定義する。
   - `Id`（一意）
   - `Title`（左メニュー表示名）
   - `Description`（折りたたみ時ツールチップなど）
   - `IconKey`（メニューアイコン文字または画像パス）
   - `Order`（並び順）
   - `CreateView()`（プラグインの `UserControl` を返す）
4. プラグイン画面として `UserControl` を新規作成する（まずは空でも可）。
5. プラグイン `csproj` に、ビルド後に DLL をリポジトリ直下 `plugins` へコピーする `Target` を追加する（`Plugin.SampleA` / `Plugin.SampleB` を参照）。
6. ソリューションにプロジェクトを追加する。
   - `dotnet sln WpfPluginHost.sln add src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj`
7. プラグインをビルドし、`plugins` に DLL が出力されることを確認してから `ShellApp` を起動し、左メニューに新項目が出ることを確認する。

注意事項：

- `Id` は全体で一意にし、メニュー対応の衝突を避ける。
- プラグインの依存バージョンはメインと揃えると読み込み衝突のリスクが下がる。
- 実行中に DLL を差し替える場合は、ビルド完了後に上書きし、書きかけファイルによる読み込み失敗を避ける。

## ビルドとデプロイ手順

### ローカル開発ビルド（推奨）

1. 復元と全体ビルド：
   - `dotnet restore WpfPluginHost.sln`
   - `dotnet build WpfPluginHost.sln -c Debug`
2. プラグインのみビルド（任意）：
   - `dotnet build src/Plugins/Plugin.SampleA/Plugin.SampleA.csproj -c Debug`
   - `dotnet build src/Plugins/Plugin.SampleB/Plugin.SampleB.csproj -c Debug`
3. メインアプリの起動：
   - `dotnet run --project src/ShellApp/ShellApp.csproj -c Debug`

### リリースビルド（Release）

1. メインアプリの発行（フレームワーク依存・配布先に .NET 8 Desktop Runtime が必要）：
   - `dotnet publish src/ShellApp/ShellApp.csproj -c Release -r win-x64 --self-contained false --output ./publish`
   - ランタイムを同梱する（配布先にインストール不要・出力サイズ大）：
   - `dotnet publish src/ShellApp/ShellApp.csproj -c Release -r win-x64 --self-contained true --output ./publish`
   - または Visual Studio の発行プロファイル：`src/ShellApp/Properties/PublishProfiles/win-x64-selfcontained.pubxml`
2. プラグインのビルド（必要な分）：
   - `dotnet build src/Plugins/Plugin.SampleA/Plugin.SampleA.csproj -c Release`
   - `dotnet build src/Plugins/Plugin.SampleB/Plugin.SampleB.csproj -c Release`
3. 推奨ディレクトリ構成：
   - `ShellApp` の発行出力（exe / dll を含む）
   - 同階層の `plugins`（**プラグインごとにサブフォルダ**で配置。例: `plugins/Plugin.SampleA/Plugin.SampleA.dll` とその依存 DLL）

例：

- `deploy/ShellApp/*`
- `deploy/plugins/Plugin.SampleA/Plugin.SampleA.dll`
- `deploy/plugins/Plugin.SampleB/Plugin.SampleB.dll`

発行ルートをすっきりさせたい場合（.NET ランタイム DLL を exe 横に大量に置きたくない）:
- `dotnet publish ... -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`（自己完結時は `--self-contained true` と併用可）
- ランタイム DLL を手動で `runtime/` などに移すのは **非推奨**（ローダが解決できず起動失敗しやすい）

### 発行後に exe をダブルクリックしても何も起きない場合

- **フレームワーク依存**（`--self-contained false`）では、配布先 PC に **[.NET 8 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/8.0)** が入っている必要があります。未インストールだとウィンドウが出ずに終了することがあります。
- ランタイムを入れたくない場合は **`--self-contained true`** で再発行してください（上記コマンドまたは `Properties/PublishProfiles/win-x64-selfcontained.pubxml`）。
- アプリが起動したあと .NET 側で未処理例外が出た場合、exe と同じフォルダに **`WPFPluginShell_startup_errors.log`** が追記されます（ランタイム自体が無い場合はこのログも作られません）。

### 本番デプロイの確認項目

- メインアプリが起動し、依存関係の欠如エラーが出ない。
- `plugins` が存在し、読み取り権限がある。
- 左メニュー数とプラグイン DLL の数が一致する。
- 特定のプラグイン DLL を差し替えたあと、画面とメニューが自動更新される（ホットリロード）。
- プラグイン DLL を削除したあと、メニュー項目が消え、メインは安定して動作する。

## ホストとの連携 (IPluginContext)

プラグインは `Initialize` メソッドで `IPluginContext` を受け取ります。これを利用して、ホスト側の UI に情報を表示できます。

- `ReportProgress`: 進捗バーを更新します。
- `ReportInfo` / `ReportSuccess` / `ReportWarning` / `ReportError`: ステータスバーにメッセージを表示します。

```csharp
public void Initialize(IPluginContext context)
{
    _context = context;
}

private async Task RunTaskAsync()
{
    _context.ReportProgress("処理を開始します...", 0, true);
    // ... 処理 ...
    _context.ReportSuccess("完了しました！");
}
```

## 新規プラグインのテンプレート（コピー用）

最小構成の例 `Plugin.SampleC` です。

### 1) `Plugin.SampleC.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Plugin.Abstractions\Plugin.Abstractions.csproj" />
  </ItemGroup>

  <Target Name="CopyPluginToRuntimeFolder" AfterTargets="Build">
    <PropertyGroup>
      <PluginDropFolder>$(MSBuildThisFileDirectory)..\..\..\plugins\$(AssemblyName)\</PluginDropFolder>
    </PropertyGroup>
    <MakeDir Directories="$(PluginDropFolder)" />
    <Copy SourceFiles="$(TargetPath)"
          DestinationFolder="$(PluginDropFolder)"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

### 2) エントリクラス `SampleCPlugin.cs`

```csharp
using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleC;

public sealed class SampleCPlugin : IPluginModule
{
    public string Id => "sampleC";
    public string Title => "サンプル C";
    public string Description => "空白ページのサンプルプラグインです。";
    public string IconKey => "🧪";
    public int Order => 30;

    public UserControl CreateView()
    {
        return new SampleCView();
    }
}
```

### 3) 空白ページ `SampleCView.xaml`

```xml
<UserControl x:Class="Plugin.SampleC.SampleCView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="#FFF8FFF8">
        <TextBlock HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="26"
                   FontWeight="SemiBold"
                   Text="プラグイン サンプル C — 空白ページ" />
    </Grid>
</UserControl>
```

### 4) コードビハインド `SampleCView.xaml.cs`

```csharp
using System.Windows.Controls;

namespace Plugin.SampleC;

public partial class SampleCView : UserControl
{
    public SampleCView()
    {
        InitializeComponent();
    }
}
```

### 5) 取り込み手順

1. `src/Plugins` に `Plugin.SampleC` を作成し、上記ファイルを配置する。
2. `dotnet sln WpfPluginHost.sln add src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj`
3. `dotnet build src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj -c Debug`
4. `ShellApp` を起動し、左メニューに「サンプル C」が表示されることを確認する。
