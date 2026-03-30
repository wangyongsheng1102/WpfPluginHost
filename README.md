# WPF Plugin Shell (.NET 8)

## 项目结构

- `src/ShellApp`：主程序壳（左侧折叠菜单 + 中央动态内容）
- `src/Plugin.Abstractions`：插件契约 `IPluginModule`
- `src/Plugins/Plugin.SampleA`：示例插件 A（空白页）
- `src/Plugins/Plugin.SampleB`：示例插件 B（空白页）
- `plugins`：运行时插件目录（热加载监控目录）

## 运行说明

1. 在 Windows 环境使用 .NET 8 SDK 打开 `WpfPluginHost.sln`。
2. 先编译插件项目，构建后会自动把插件 DLL 复制到根目录 `plugins`。
3. 启动 `ShellApp` 后，左侧菜单将按插件数量自动显示对应入口。
4. 运行时替换 `plugins` 目录下 DLL，会自动触发热加载刷新。

## 添加 Plugin 手顺

以下步骤用于新增一个可被主程序动态加载的插件 DLL。

1. 在 `src/Plugins` 下创建新的 Class Library（WPF）项目，例如 `Plugin.SampleC`。
2. 修改插件项目 `csproj`：
   - 目标框架使用 `net8.0-windows`
   - 开启 `<UseWPF>true</UseWPF>`
   - 引用 `src/Plugin.Abstractions/Plugin.Abstractions.csproj`
3. 在插件项目中实现 `IPluginModule`，至少包含：
   - `Id`（唯一）
   - `Title`（左侧菜单名称）
   - `IconKey`（菜单图标字符）
   - `Order`（菜单排序）
   - `CreateView()`（返回插件 `UserControl`）
4. 新建一个插件页面（`UserControl`），当前可先做空白页面用于展示挂载效果。
5. 在插件项目 `csproj` 中增加构建后复制 DLL 到根目录 `plugins` 的 `Target`（可参考 `Plugin.SampleA` / `Plugin.SampleB`）。
6. 将新插件项目加入解决方案：
   - `dotnet sln WpfPluginHost.sln add src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj`
7. 编译该插件后确认 `plugins` 目录出现对应 DLL，运行 `ShellApp` 可自动显示新的左侧菜单项。

注意事项：
- `Id` 必须全局唯一，避免菜单映射冲突。
- 插件依赖建议与主程序保持一致版本，减少加载冲突风险。
- 插件运行时替换 DLL 时，建议先确保构建完成再覆盖，避免半写入文件触发失败加载。

## 编译与部署手顺

### 本地开发编译（推荐）

1. 还原并编译全部项目：
   - `dotnet restore WpfPluginHost.sln`
   - `dotnet build WpfPluginHost.sln -c Debug`
2. 单独编译插件（可选）：
   - `dotnet build src/Plugins/Plugin.SampleA/Plugin.SampleA.csproj -c Debug`
   - `dotnet build src/Plugins/Plugin.SampleB/Plugin.SampleB.csproj -c Debug`
3. 启动主程序：
   - `dotnet run --project src/ShellApp/ShellApp.csproj -c Debug`

### 发布构建（Release）

1. 发布主程序：
   - `dotnet publish src/ShellApp/ShellApp.csproj -c Release -r win-x64 --self-contained false`
2. 发布插件（按需发布多个）：
   - `dotnet build src/Plugins/Plugin.SampleA/Plugin.SampleA.csproj -c Release`
   - `dotnet build src/Plugins/Plugin.SampleB/Plugin.SampleB.csproj -c Release`
3. 部署目录建议结构：
   - `ShellApp` 发布输出目录（包含 exe / dll）
   - 同级 `plugins` 目录（放置所有插件 DLL）

示例：
- `deploy/ShellApp/*`
- `deploy/plugins/Plugin.SampleA.dll`
- `deploy/plugins/Plugin.SampleB.dll`

### 生产部署检查清单

- 主程序可启动，且不报缺失依赖。
- `plugins` 目录存在且有读取权限。
- 左侧菜单数量与插件 DLL 数量一致。
- 替换某个插件 DLL 后，页面与菜单可自动刷新（热加载生效）。
- 删除某个插件 DLL 后，菜单项自动消失且主程序保持稳定。

## 新插件模板代码（可直接复制）

下面给一个最小可用示例 `Plugin.SampleC`，用于快速新增插件。

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
      <PluginDropFolder>$(MSBuildThisFileDirectory)..\..\..\plugins\</PluginDropFolder>
    </PropertyGroup>
    <MakeDir Directories="$(PluginDropFolder)" />
    <Copy SourceFiles="$(TargetPath)"
          DestinationFolder="$(PluginDropFolder)"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

### 2) 插件入口类 `SampleCPlugin.cs`

```csharp
using Plugin.Abstractions;
using System.Windows.Controls;

namespace Plugin.SampleC;

public sealed class SampleCPlugin : IPluginModule
{
    public string Id => "sampleC";
    public string Title => "Sample C";
    public string IconKey => "🧪";
    public int Order => 30;

    public UserControl CreateView()
    {
        return new SampleCView();
    }
}
```

### 3) 空白页面 `SampleCView.xaml`

```xml
<UserControl x:Class="Plugin.SampleC.SampleCView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="#FFF8FFF8">
        <TextBlock HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="26"
                   FontWeight="SemiBold"
                   Text="Plugin Sample C - Blank Page" />
    </Grid>
</UserControl>
```

### 4) 页面后台 `SampleCView.xaml.cs`

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

### 5) 接入步骤

1. 在 `src/Plugins` 下创建 `Plugin.SampleC` 并放入上述文件。
2. 执行：`dotnet sln WpfPluginHost.sln add src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj`
3. 编译：`dotnet build src/Plugins/Plugin.SampleC/Plugin.SampleC.csproj -c Debug`
4. 启动 `ShellApp`，左侧菜单应自动出现 `Sample C`。
