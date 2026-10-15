using System.IO;
using System.Reflection;
using RoBIM.Manager.Core;

namespace RoBIM.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var quiet = args.Contains("--install-quiet");
        var extract = Array.IndexOf(args, "--extract");
        try
        {
            if (extract >= 0)
            {
                var destination = Path.GetFullPath(args[extract + 1]);
                if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                    throw new IOException("압축 해제 대상 폴더는 비어 있어야 합니다.");
                Extract(destination);
                return 0;
            }
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RoBIM Manager");
            if (!quiet && MessageBox.Show("RoBIM Manager를 설치합니다.\n\n설치 위치: " + installed +
                "\nAutoCAD·Revit 도구는 설치 후 관리자에서 선택합니다.\n\n실행에 필요한 .NET 런타임이 포함되어 있습니다.\n설치를 계속하시겠습니까?",
                "RoBIM Manager 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return 0;
            var temporary = Path.Combine(Path.GetTempPath(), "RoBIM-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                Extract(temporary);
                var script = Path.Combine(temporary, "RoBIM-Manager", "Install.ps1");
                var result = ProcessRunner.Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Quiet"])
                    .GetAwaiter().GetResult();
                if (result.ExitCode != 0) throw new IOException("설치 실패. RoBIM Manager를 종료하고 설치 위치의 쓰기 권한을 확인하십시오.\n" + result.Error);
                if (!quiet) MessageBox.Show("설치가 완료되었습니다.\n시작 메뉴 또는 바탕화면의 RoBIM Manager를 실행하십시오.", "RoBIM Manager");
                return 0;
            }
            finally { SafeFiles.DeleteOwnedTree(Path.GetTempPath(), temporary); }
        }
        catch (Exception ex)
        {
            if (extract < 0 && !quiet) MessageBox.Show(ex.Message, "RoBIM Manager 설치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void Extract(string destination)
    {
        Directory.CreateDirectory(destination);
        var archive = Path.Combine(destination, ".payload.zip");
        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoBIM.Payload.zip")!)
        using (var output = File.Create(archive)) input.CopyTo(output);
        try { SafeFiles.Extract(archive, destination); }
        finally { File.Delete(archive); }
    }
}
