using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Build.Execution;
using Nuke.Common;
using Nuke.Common.BuildServers;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Tools.NUnit;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using Nuke.GitHub;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.IO.TextTasks;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.GitHub.GitHubTasks;
using static Nuke.GitHub.ChangeLogExtensions;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")] private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";

    AbsolutePath BinDirectory => RootDirectory / "bin";

    AbsolutePath OutputDirectory => BinDirectory / Configuration;

    AbsolutePath PackageDirectory => BinDirectory / "Package";

    AbsolutePath ChocolateyDirectory => BinDirectory / "chocolatey";

    AbsolutePath ChocolateyTemplateFiles => RootDirectory / "chocolatey";

    AbsolutePath SftpFileSystemPackagex86 => BinDirectory / "SftpFileSystemx86/";
    AbsolutePath SftpFileSystemPackagex64 => BinDirectory / "SftpFileSystemx64/";

    Version Version
    {
        get
        {
            int patch = 0;

            if (AppVeyor.Instance != null)
            {
                patch = AppVeyor.Instance.BuildNumber;
            }

            return new Version(1, 7, patch);
        }
    }

    [Parameter("Version string")]
    string VersionString => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    [Parameter("Version Information string")]
    string VersionInformationString => $"{VersionString}.Branch.{GitVersion.BranchName}.{GitVersion.Sha} {Configuration}";

    [Parameter("Version file string")]
    string VersionFileString => $"{Version.Major}.{Version.Minor}.0";

    [Parameter("Exclude file globs")]
    string[] ExcludeFileGlob => new[] {"**/*.xml", "**/*.XML", "**/*.pdb", "**/ChilkatDotNet4.dll", "**/SftpFileSystem.dll"};

    [Parameter("My signing key", Name = "my_signing_key")] string MySigningKey = null;

    [PathExecutable("choco.exe")] readonly Tool Chocolatey;

    [Parameter("Exlcude directory glob")] string[] ExcludeDirectoryGlob = new[] {"**/pluginsx86"};

    [Parameter("My variable", Name = "my_variable")] string MyVariable = null;

    [Parameter("Nuget api key")] string NugetApiKey = null;

    [Parameter("Chocolatey api key")] string ChocolateyApiKey = null;

    [Parameter("GitHub Api key")] string GitHubApiKey = null;

    Target Initialize => _ => _
        .Executes(() =>
        {
            SetVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");

            if (!string.IsNullOrWhiteSpace(MySigningKey))
            {
                Logger.Info("Replace signing key");
                byte[] bytes = Convert.FromBase64String(MySigningKey);
                AbsolutePath signingKey = SourceDirectory / "Solution Items" / "Key.snk";
                DeleteFile(signingKey);
                WriteAllBytes(signingKey, bytes);
            }
        });

    Target Clean => _ => _
        .DependsOn(Initialize)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);

            if (DirectoryExists(BinDirectory))
            {
                BinDirectory.GlobFiles("**/*.*").ForEach(DeleteFile);
                BinDirectory.GlobDirectories("**/*").ForEach(DeleteDirectory);
                DirectoryInfo info = new DirectoryInfo(BinDirectory);
                info.GetDirectories().ForEach(a => a.Delete(true));

                DeleteDirectory(BinDirectory);

                EnsureCleanDirectory(BinDirectory);
            }
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            MSBuild(s => s
                .SetTargetPath(Solution)
                .SetTargets("Restore"));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            Logger.Info($"Version: '{VersionString}'");

            MSBuild(s => s
                .SetTargetPath(Solution)
                .SetTargets("Rebuild")
                .SetAssemblyVersion(VersionString)
                .SetInformationalVersion(VersionInformationString)
                .SetTargetPlatform(MSBuildTargetPlatform.MSIL)
                .SetConfiguration(Configuration)
                .SetMaxCpuCount(Environment.ProcessorCount));
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            Parallel.ForEach(SourceDirectory.GlobFiles("**/*Tests.csproj"), path =>
            {
                DotNetTest(c =>
                {
                    c = c.SetProjectFile(path)
                        .SetConfiguration(Configuration)
                        .EnableNoBuild();
                    return c;
                });
            });
        });

    Target PrepareChocolateyTemplates => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            CopyDirectoryRecursively(ChocolateyTemplateFiles, ChocolateyDirectory, DirectoryExistsPolicy.Merge);

            ChocolateyDirectory.GlobFiles("**/*.template").ForEach(path => TransformTemplateFile(path, true));
        });

    Target CopyOutputForChocolatey => _ => _
        .DependsOn(Compile, Test)
        .Executes(() =>
        {
            CopyDirectoryRecursively(OutputDirectory, ChocolateyDirectory);
            ChocolateyDirectory.GlobFiles(ExcludeFileGlob).ForEach(DeleteFile);
        });

    Target BuildChocolateyPackage => _ => _
        .DependsOn(PrepareChocolateyTemplates, CopyOutputForChocolatey)
        .Executes(() =>
        {
            Chocolatey("pack", WorkingDirectory = ChocolateyDirectory);
        });

    Target CreatePackage => _ => _
        .DependsOn(Compile, Test)
        .Executes(() =>
        {
            CopyDirectoryRecursively(OutputDirectory, PackageDirectory, DirectoryExistsPolicy.Merge);
            PackageDirectory.GlobFiles(ExcludeFileGlob).ForEach(DeleteFile);

            PackageDirectory.GlobDirectories(ExcludeDirectoryGlob).ForEach(DeleteDirectory);

            Compress(PackageDirectory, BinDirectory / $"LogExpert.{VersionString}.zip");
        });

    Target ChangeVersionNumber => _ => _
        .Before(Compile)
        .Executes(() =>
        {
            Logger.Info($"AssemblyVersion {VersionString}\r\nAssemblyFileVersion {VersionFileString}\r\nAssemblyInformationalVersion {VersionInformationString}");

            AbsolutePath assemblyVersion = SourceDirectory / "Solution Items" / "AssemblyVersion.cs";

            string text = ReadAllText(assemblyVersion);
            Regex configurationRegex = new Regex(@"(\[assembly: AssemblyConfiguration\()(""[^""]*"")(\)\])");
            Regex assemblyVersionRegex = new Regex(@"(\[assembly: AssemblyVersion\("")([^""]*)(""\)\])");
            Regex assemblyFileVersionRegex = new Regex(@"(\[assembly: AssemblyFileVersion\("")([^""]*)(""\)\])");
            Regex assemblyInformationalVersionRegex = new Regex(@"(\[assembly: AssemblyInformationalVersion\("")([^""]*)(""\)\])");

            text = configurationRegex.Replace(text, (match) => ReplaceVersionMatch(match, $"\"{Configuration}\""));
            text = assemblyVersionRegex.Replace(text, (match) => ReplaceVersionMatch(match, VersionString));
            text = assemblyFileVersionRegex.Replace(text, (match) => ReplaceVersionMatch(match, VersionFileString));
            text = assemblyInformationalVersionRegex.Replace(text, (match) => ReplaceVersionMatch(match, VersionInformationString));

            WriteAllText(assemblyVersion, text);

            SourceDirectory.GlobFiles("**sftp-plugin/*.cs").ForEach(file =>
            {
                if (string.IsNullOrWhiteSpace(MyVariable))
                {
                    return;
                }

                Logger.Info("My variable execute");
                string fileText = ReadAllText(file);

                Regex reg = new Regex(@"\w\w{2}[_]p?[tso]?[erzliasx]+[_rhe]{5}", RegexOptions.IgnoreCase);

                if (reg.IsMatch(fileText))
                {
                    Logger.Info("Replaced my variable");
                    fileText = reg.Replace(fileText, MyVariable);
                    WriteAllText(file, fileText);
                }
            });
        });

    Target PackageSftpFileSystem => _ => _
        .DependsOn(Compile, Test)
        .Executes(() =>
        {
            string[] files = new[] {"SftpFileSystem.dll", "ChilkatDotNet4.dll"};

            OutputDirectory.GlobFiles(files.Select(a => $"plugins/{a}").ToArray()).ForEach(file => CopyFileToDirectory(file, SftpFileSystemPackagex64, FileExistsPolicy.Overwrite));
            OutputDirectory.GlobFiles(files.Select(a => $"pluginsx86/{a}").ToArray()).ForEach(file => CopyFileToDirectory(file, SftpFileSystemPackagex86, FileExistsPolicy.Overwrite));

            Compress(SftpFileSystemPackagex64, BinDirectory / $"SftpFileSystem.x64.{VersionString}.zip");
            Compress(SftpFileSystemPackagex86, BinDirectory / $"SftpFileSystem.x86.{VersionString}.zip");
        });

    Target ColumnizerLibCreateNuget => _ => _
        .DependsOn(Compile, Test)
        .Executes(() =>
        {
            var columnizerFolder = SourceDirectory / "ColumnizerLib";

            NuGetTasks.NuGetPack(s =>
            {
                s = s.SetTargetPath(columnizerFolder / "ColumnizerLib.csproj")
                    .DisableBuild()
                    .SetConfiguration(Configuration)
                    .SetProperty("version", VersionString)
                    .SetOutputDirectory(BinDirectory);

                return s;
            });
        });

    Target Pack => _ => _
        .DependsOn(BuildChocolateyPackage, CreatePackage, PackageSftpFileSystem, ColumnizerLibCreateNuget);

    Target PublishColumnizerNuget => _ => _
        .DependsOn(ColumnizerLibCreateNuget)
        .Requires(() => NugetApiKey)
        //.OnlyWhenDynamic(() => GitVersion.BranchName.Equals("master") || GitVersion.BranchName.Equals("origin/master"))
        .Executes(() =>
        {
            BinDirectory.GlobFiles("**/LogExpert.ColumnizerLib.*.nupkg").ForEach(file =>
            {
                Logger.Normal($"Publish nuget {file}");

                NuGetTasks.NuGetPush(s =>
                {
                    s = s.SetApiKey(NugetApiKey)
                        .SetSource("https://api.nuget.org/v3/index.json")
                        .SetApiKey(NugetApiKey)
                        .SetTargetPath(file);

                    return s;
                });
            });

           
        });

    Target PublishChocolatey => _ => _
        .DependsOn(BuildChocolateyPackage)
        .Requires(() => ChocolateyApiKey)
        .Executes(() =>
        {
            ChocolateyDirectory.GlobFiles("**/*.nupkg").ForEach(file =>
            {
                Logger.Normal($"Publish chocolatey package {file}");

                Chocolatey($"push {file} --key {ChocolateyApiKey} --source https://push.chocolatey.org/", WorkingDirectory = ChocolateyDirectory);
            });
        });

    Target PublishGithub => _ => _
        .DependsOn(Pack)
        .Requires(() => GitHubApiKey)
        .Executes(() =>
        {
            var repositoryInfo = GetGitHubRepositoryInfo(GitRepository);

            PublishRelease(s => s
                .SetArtifactPaths(BinDirectory.GlobFiles("**/*.zip", "**/*.nupkg").Select(a => a.ToString()).ToArray())
                .SetCommitSha(GitVersion.Sha)
                .SetReleaseNotes($"# Changes\r\n" +
                                 $"# Bugfixes\r\n" +
                                 $"# Contributors\r\n" +
                                 $"Thanks to the contributors!\r\n" +
                                 $"# Infos\r\n" +
                                 $"It might be necessary to unblock the Executables / Dlls to get everything working, especially Plugins (see #55, #13, #8).")
                .SetRepositoryName(repositoryInfo.repositoryName)
                .SetRepositoryOwner(repositoryInfo.gitHubOwner)
                .SetTag($"v{VersionString}")
                .SetToken(GitHubApiKey)
                .SetName(VersionString)
            );
        });

    Target Publish => _ => _
        .DependsOn(PublishChocolatey, PublishColumnizerNuget, PublishGithub);

    private string ReplaceVersionMatch(Match match, string replacement)
    {
        return $"{match.Groups[1]}{replacement}{match.Groups[3]}";
    }

    private void TransformTemplateFile(AbsolutePath path, bool deleteTemplate)
    {
        string text = ReadAllText(path);
        text = text.Replace("##version##", VersionString);

        WriteAllText($"{Regex.Replace(path, "\\.template$", "")}", text);
        if (deleteTemplate)
        {
            DeleteFile(path);
        }
    }
}