using Microsoft.Win32;
using Rebex.IO.Compression;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SigerBackup
{
    internal class Program
    {
        private static string App = Assembly.GetEntryAssembly().Location;
        private static string timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
        private static FolderBrowserDialog backup_dir;
        private static RegistryKey siger_backup_modified;
        private static RegistryKey siger_backup;
        private static string[] args;

        const string keyName = @"Directory\Background\shell\";

        [STAThread]
        static void Main(string[] app_args)
        {
            if (app_args.Length == 2 && app_args[1] != string.Empty)
                Directory.SetCurrentDirectory(app_args[1]);

            args = app_args;
            setTitle();

            if (args.Count() > 0 && (args[0] == "-u" || args[0] == "--uninstall"))
                Uninstall();

            siger_backup_modified = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackupModified", true);
            siger_backup = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackup", true);

            if (siger_backup == null || siger_backup_modified == null)
                Install();

            Run();
        }

        public static void Run()
        {
            try
            {
                var cmd_backup_modified = siger_backup_modified.OpenSubKey("command");
                var cmd_backup = siger_backup.OpenSubKey("command");

                Console.Clear();

                if (
                    (cmd_backup.GetValue(null) != null && cmd_backup.GetValue(null).ToString().Contains(App)) &&
                    (cmd_backup_modified != null && cmd_backup_modified.GetValue(null).ToString().Contains(App))
                )
                {
                    setTitle("Executando...");

                    if (args.Length > 1)
                    {
                        var param = args[0].ToLower();

                        switch (param)
                        {
                            case "-b":
                            case "--backup":
                                setTitle("Efetuando backup completo");
                                Console.WriteLine("Efetuando backup...\n");
                                CompressFile("backup");
                                break;

                            case "-m":
                            case "--modified":
                                setTitle("Efetuando backup apenas modificados");
                                Console.WriteLine("Efetuando backup de arquivos modificados...\n");
                                CompressFile("modified");
                                break;
                        }

                        siger_backup_modified.Close();
                        siger_backup.Close();
                        exitApp();
                    }
                }
                else
                    Install();

                if (args.Length < 2)
                    Environment.Exit(0);
            }
            catch { }
        }

        public static void setTitle(string title = "")
        {
            Console.Title = "Siger Backup" + (title == "" ? "" : $": {title}");
        }

        public static void Install()
        {
            if (!IsAdministrator())
            {
                setTitle("Executando como Administrador...");
                Thread.Sleep(1);
                RunAsAdmin();
            }
            else
            {
                setTitle("Instalando...");
                Console.WriteLine("Criando chaves no registro do Windows...");

                backup_dir = new FolderBrowserDialog();

                backup_dir.Description = "Por favor, escolha o local onde irá salvar os backup's";
                backup_dir.ShowNewFolderButton = false;

                var run = false;

                try
                {
                    var cmd_backup_modified = siger_backup_modified.OpenSubKey("command");
                    var cmd_backup = siger_backup.OpenSubKey("command");

                    if (cmd_backup != null && cmd_backup_modified != null)
                    {
                        if (!cmd_backup_modified.GetValue(null).ToString().Contains(App))
                            siger_backup_modified = makeKey("SigerBackupModified", "Siger Backup [Modified]");

                        if (!cmd_backup.GetValue(null).ToString().Contains(App))
                            siger_backup = makeKey("SigerBackup", "Siger Backup");

                        run = true;
                    }
                }
                catch
                {
                    if (backup_dir.ShowDialog() == DialogResult.OK)
                    {
                        if (siger_backup_modified == null)
                            siger_backup_modified = makeKey("SigerBackupModified", "Siger Backup [Modified]");

                        if (siger_backup == null)
                            siger_backup = makeKey("SigerBackup", "Siger Backup");

                        setTitle("Instalação concluída!");
                        Console.Clear();
                        Console.WriteLine("Detalhes da instalação:");
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Write($"  Diretório de backup: ");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine(backup_dir.SelectedPath);
                        Console.ResetColor();
                        Console.WriteLine("\nMenu de Contexto:");
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Write("  Backup completo: ");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Siger Backup");
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Write("  Backup apenas modificados: ");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Siger Backup [Modified]");

                        if (args.Length > 0)
                        {
                            pauseApp();
                            run = true;
                        }
                        else
                            exitApp();
                    }
                }

                if (run)
                {
                    Thread.Sleep(1);
                    Run();
                }
            }
        }

        public static void Uninstall()
        {
            if (!IsAdministrator())
            {
                setTitle("Executando como Administrador...");
                Thread.Sleep(1);
                RunAsAdmin();
            }
            else
            {
                setTitle("Desinstalando...");

                Thread.Sleep(1);

                RegistryKey main = Registry.ClassesRoot.OpenSubKey(keyName, true);

                main.DeleteSubKeyTree("SigerBackupModified", false);
                main.DeleteSubKeyTree("SigerBackup", false);
                main.Close();

                Console.WriteLine("Desinstalado com sucesso!");
                Thread.Sleep(1250);
            }

            Environment.Exit(0);
        }

        public static RegistryKey makeKey(string name, string title)
        {
            RegistryKey key = Registry.ClassesRoot.CreateSubKey($"{keyName}{name}");

            RegistryKey cmd = key.CreateSubKey("command");

            if (backup_dir.SelectedPath != string.Empty)
                key.SetValue("Backup Dir", backup_dir.SelectedPath, RegistryValueKind.String);

            key.SetValue("MUIVerb", $"{title}", RegistryValueKind.String);
            key.SetValue("Icon", App, RegistryValueKind.String);

            var param = (name == "SigerBackup") ? "backup" : "modified";

            cmd.SetValue("", $"\"{App}\" --{param} \"%V\"");
            
            return key;
        }

        public static void RunAsAdmin()
        {
            setTitle("Executando como Administrador...");

            Thread.Sleep(1);

            ProcessStartInfo process = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Arguments = string.Join(" ", args),
                FileName = App,
                Verb = "runas"
            };

            try
            {
                Process.Start(process);
            }
            catch
            {
                return;
            }

            Environment.Exit(0);
        }

        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void CompressFile(string cmd)
        {
            string output = string.Empty;
            string modified = string.Empty;
            List<string> allow_list = new List<string>();
            string filename = Path.Combine(siger_backup.GetValue("Backup Dir").ToString(), Path.GetFileName($"{args[1]}{getVersion()}_{timestamp}.zip"));

            if (cmd == "backup")
            {
                string[] skip = new[] { ".rar", ".env", @"\.git\", ".gitignore", ".gitattributes", $"{Path.GetFileName(args[1])}.lst" };

                allow_list = Directory.GetFiles(args[1], "*", SearchOption.AllDirectories).ToList();

                foreach (string s in skip)
                    allow_list = allow_list.Where(a => !a.Contains(s)).ToList();
            }
            else if (cmd == "modified")
            {
                modified = " (Modified)";
                output += runCommand("git.exe", "diff --name-only --diff-filter=M");
                output += runCommand("git.exe", "ls-files -o --exclude-standard");

                allow_list = output.Split(
                    new string[] { "\r\n", "\r", "\n" },
                    StringSplitOptions.None
                ).ToList();
            }

            try
            {
                int total = 0;

                if (File.Exists(filename))
                    File.Delete(filename);

                var zip = new ZipArchive(filename);
                zip.CompressionMethod = CompressionMethod.EnhancedDeflate;
                zip.CompressionLevel = 9;

                allow_list.Sort();
                //Console.WriteLine(String.Join("\r\n", allow_list));
                //Console.ReadLine();

                foreach (string f in allow_list)
                {
                    if (File.Exists(f))
                    {
                        string file = f.Replace($@"{args[1]}\", "").Replace("\\", "/");
                        string file_path = Path.GetDirectoryName(file) != "" ? Path.GetDirectoryName(file) : "/";
                        Console.WriteLine($"Compactando arquivo {file}");
                        zip.Add(file, file_path);
                        total++;
                    }
                }

                zip.Close();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n  Foi feito backup{modified} de {total} arquivos.");
            }
            catch (Exception ex)
            {
                siger_backup_modified.Close();
                siger_backup.Close();

                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Ocorreu um erro:");
                Console.WriteLine(ex.Message);
                exitApp();
            }
        }

        static string runCommand(string file, string args)
        {
            Process process = new Process
            {
                StartInfo =
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            //* Read the output (or the error)
            string output = process.StandardOutput.ReadToEnd();
            string err = process.StandardError.ReadToEnd();
            if (err != "")
            {
                Console.Clear();
                Console.WriteLine($"ERROR[{err}]");
                siger_backup_modified.Close();
                siger_backup.Close();
                exitApp();
            }
            process.WaitForExit();
            return output;
        }

        public static string getVersion()
        {
            string version = string.Empty;
            string fileversion = string.Empty;

            foreach(var key in new string[] { "SIGER.php", "APP.php", "SGA.php" })
            {
                if (File.Exists($@"{args[1]}\core\{key}"))
                {
                    fileversion = $@"{args[1]}\core\{key}";
                    break;
                }
            }

            if (fileversion != string.Empty)
            {
                var lines = File.ReadAllLines(fileversion);

                if (lines.Any(t => t.Contains("VERSION")))
                    version = "_v" + lines.Where(t => t.Contains("VERSION")).First().Split('"')[1];
            }

            return version;
        }

        public static void pauseApp()
        {
            Console.ResetColor();
            Console.WriteLine("\nPressione qualquer tecla para continuar...");
            Console.ReadKey(true);
        }

        public static void exitApp()
        {
            Console.ResetColor();
            Console.WriteLine("\nPressione qualquer tecla para sair...");
            Console.ReadKey(true);
            Environment.Exit(0);
        }
    }
}
