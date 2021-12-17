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
        private static RegistryKey siger_backup_modified_bg;
        private static RegistryKey siger_backup_modified;
        private static RegistryKey siger_backup_bg;
        private static RegistryKey siger_backup;
        private static string[] args;

        const string keyNameBg = @"Directory\Background\shell\";
        const string keyName = @"Directory\shell\";

        [STAThread]
        static void Main(string[] app_args)
        {
            args = app_args;
            setTitle();

            if (args.Count() > 0 && (args[0] == "-u" || args[0] == "--uninstall"))
                Uninstall();

            siger_backup_modified_bg = Registry.ClassesRoot.OpenSubKey($"{keyNameBg}SigerBackupModified", true);
            siger_backup_modified = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackupModified", true);
            siger_backup_bg = Registry.ClassesRoot.OpenSubKey($"{keyNameBg}SigerBackup", true);
            siger_backup = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackup", true);

            if (siger_backup_bg == null || siger_backup_modified_bg == null || siger_backup == null || siger_backup_modified == null)
                Install();

            Run();
        }

        public static void Run()
        {
            try
            {
                if (args.Length == 2 && args[1] != string.Empty)
                    Directory.SetCurrentDirectory(args[1]);
            }
            catch
            {
                help();
            }

            try
            {
                var cmd_backup_modified_bg = siger_backup_modified_bg.OpenSubKey("command");
                var cmd_backup_modified = siger_backup_modified.OpenSubKey("command");
                var cmd_backup_bg = siger_backup_bg.OpenSubKey("command");
                var cmd_backup = siger_backup.OpenSubKey("command");

                Console.Clear();

                if (
                    (cmd_backup_modified_bg != null && cmd_backup_modified_bg.GetValue(null).ToString().Contains(App)) &&
                    (cmd_backup_bg.GetValue(null) != null && cmd_backup_bg.GetValue(null).ToString().Contains(App)) &&
                    (cmd_backup_modified != null && cmd_backup_modified.GetValue(null).ToString().Contains(App)) &&
                    (cmd_backup.GetValue(null) != null && cmd_backup.GetValue(null).ToString().Contains(App))
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

                            default:
                                help();
                                break;
                        }

                        siger_backup_modified.Close();
                        siger_backup.Close();
                        exitApp();
                    }
                    else
                        help();
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
                Console.ForegroundColor = ConsoleColor.DarkGreen;

                backup_dir = new FolderBrowserDialog();

                backup_dir.Description = "Por favor, escolha o local onde irá salvar os backup's";
                backup_dir.ShowNewFolderButton = false;

                try
                {
                    var cmd_backup_modified_bg = siger_backup_modified_bg.OpenSubKey("command");
                    var cmd_backup_modified = siger_backup_modified.OpenSubKey("command");
                    var cmd_backup_bg = siger_backup_bg.OpenSubKey("command");
                    var cmd_backup = siger_backup.OpenSubKey("command");

                    if (cmd_backup != null && cmd_backup_modified != null && null != cmd_backup_modified_bg && null != cmd_backup_bg)
                    {
                        Console.WriteLine("Atualizando chaves no registro do Windows...");

                        if (!cmd_backup_modified.GetValue(null).ToString().Contains(App) || !cmd_backup_modified_bg.GetValue(null).ToString().Contains(App))
                        {
                            var key = makeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            siger_backup_modified_bg = key["backup_bg"];
                            siger_backup_modified = key["backup"];
                        }

                        if (!cmd_backup.GetValue(null).ToString().Contains(App) || !cmd_backup_bg.GetValue(null).ToString().Contains(App))
                        {
                            var key = makeKey("SigerBackup", "Siger Backup");
                            siger_backup_bg = key["backup_bg"];
                            siger_backup = key["backup"];
                        }

                        Console.WriteLine("Registro atualizado com êxito!");
                        pauseApp();
                    }
                }
                catch
                {
                    Console.WriteLine("Criando chaves no registro do Windows...");

                    if (backup_dir.ShowDialog() == DialogResult.OK)
                    {
                        if (siger_backup_modified == null || null == siger_backup_modified_bg)
                        {
                            var key = makeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            siger_backup_modified_bg = key["backup_bg"];
                            siger_backup_modified = key["backup"];
                        }

                        if (siger_backup == null || null == siger_backup_bg)
                        {
                            var key = makeKey("SigerBackup", "Siger Backup");
                            siger_backup_bg = key["backup_bg"];
                            siger_backup = key["backup"];
                        }

                        setTitle("Instalação concluída!");
                        Console.Clear();
                        log("Detalhes da instalação:", "Diretório de backup: ", backup_dir.SelectedPath);
                        log("Menu de Contexto:", "Backup completo:|Backup apenas modificados:", " Siger Backup| Siger Backup [Modified] (git)");

                        pauseApp();
                    }
                }

                Run();
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

                RegistryKey main_bg = Registry.ClassesRoot.OpenSubKey(keyNameBg, true);
                RegistryKey main = Registry.ClassesRoot.OpenSubKey(keyName, true);

                main_bg.DeleteSubKeyTree("SigerBackupModified", false);
                main_bg.DeleteSubKeyTree("SigerBackup", false);
                main_bg.Close();

                main.DeleteSubKeyTree("SigerBackupModified", false);
                main.DeleteSubKeyTree("SigerBackup", false);
                main.Close();

                Console.WriteLine("Desinstalado com sucesso!");
                Thread.Sleep(1250);
            }

            Environment.Exit(0);
        }

        public static Dictionary<string, RegistryKey> makeKey(string name, string title)
        {
            RegistryKey key_bg = Registry.ClassesRoot.CreateSubKey($"{keyNameBg}{name}");
            RegistryKey key = Registry.ClassesRoot.CreateSubKey($"{keyName}{name}");

            RegistryKey cmd_bg = key_bg.CreateSubKey("command");
            RegistryKey cmd = key.CreateSubKey("command");

            if (backup_dir.SelectedPath != string.Empty)
            {
                key_bg.SetValue("Backup Dir", backup_dir.SelectedPath, RegistryValueKind.String);
                key.SetValue("Backup Dir", backup_dir.SelectedPath, RegistryValueKind.String);
            }

            key_bg.SetValue("MUIVerb", $"{title}", RegistryValueKind.String);
            key.SetValue("MUIVerb", $"{title}", RegistryValueKind.String);
            key_bg.SetValue("Icon", App, RegistryValueKind.String);
            key.SetValue("Icon", App, RegistryValueKind.String);

            var param = (name == "SigerBackup") ? "backup" : "modified";

            cmd_bg.SetValue("", $"\"{App}\" --{param} \"%V\"");
            cmd.SetValue("", $"\"{App}\" --{param} \"%V\"");

            var dict = new Dictionary<string, RegistryKey>();
            dict["backup_bg"] = key_bg;
            dict["backup"] = key;

            return dict;
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
                modified = " (modified)";
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
                bool run_zip = false;

                allow_list.Sort();

                foreach (string f in allow_list)
                {
                    if (File.Exists(f))
                    {
                        run_zip = true;
                        break;
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkYellow;

                if (File.Exists(filename))
                    File.Delete(filename);

                if (run_zip)
                {
                    var zip = new ZipArchive(filename);
                    zip.CompressionMethod = CompressionMethod.EnhancedDeflate;
                    zip.CompressionLevel = 9;

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
                else
                    Console.WriteLine($"  Não há arquivos para fazer backup{modified}.");
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

        public static void log(string title, string _keys, string _values, string _desc = "")
        {
            var desc = _desc.Split('|');
            var keys = _keys.Split('|');
            var values = _values.Split('|');

            Console.ResetColor();
            Console.WriteLine($"{title}{(title.GetLast(1) != ":" ? ":" : "")}");

            for (var i = 0; i < keys.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write($"  {keys[i]}");
                Console.ForegroundColor = ConsoleColor.DarkYellow;

                if (desc.ElementAtOrDefault(i) != null)
                {
                    Console.Write($" {values[i]}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($" {desc[i]}");
                }
                else
                {
                    Console.WriteLine($" {values[i]}");
                }
            }

            Console.WriteLine();
        }

        public static void help()
        {
            setTitle("Como usar..");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Faz backup (.zip) de uma pasta para o local escolhido na instalação.\n");

            log("Opções:",
                "[-u, --uninstall]|[-m, --modified]|[-b, --backup]",
                "\tDesinstala esta aplicação.|\tEfetua o backup dos arquivos modificados e/ou não rastreável.|\tEfetua o backup completo.",
                "Remove as chaves do registro do Windows.|Apenas git!"
            );

            Console.ResetColor();
            Console.WriteLine("Exemplo:");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  {AppDomain.CurrentDomain.FriendlyName} ");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write("--backup ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\"" + siger_backup.GetValue("Backup Dir") + "\"");
            Console.WriteLine();
            exitApp();
        }

        public static void pauseApp()
        {
            Console.ResetColor();
            Console.Write("\nPressione qualquer tecla para continuar...\n");
            Console.ReadKey(true);
        }

        public static void exitApp()
        {
            Console.ResetColor();
            Console.Write("\nPressione qualquer tecla para sair...\n");
            Console.ReadKey(true);
            Environment.Exit(0);
        }
    }
}