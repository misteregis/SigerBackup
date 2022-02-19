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
        private static readonly string App = Assembly.GetEntryAssembly().Location;
        private static readonly string timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
        private static readonly string version = Application.ProductVersion;
        private static readonly Dictionary<string, string> args = new Dictionary<string, string>();
        private static readonly List<string> excludes = new List<string>();
        private static FolderBrowserDialog backup_dir;
        private static RegistryKey siger_backup_modified_bg;
        private static RegistryKey siger_backup_modified;
        private static RegistryKey siger_backup_bg;
        private static RegistryKey siger_backup;

        const string keyNameBg = @"Directory\Background\shell\";
        const string keyName = @"Directory\shell\";

        [STAThread]
        static void Main()
        {
            string[] app_args = Environment.GetCommandLineArgs();
            for (var i = 1; i < app_args.Length; i += 2)
            {
                if (app_args[i].StartsWith("-"))
                {
                    var key = app_args[i].StartsWith("--") ? app_args[i].Substring(2) : app_args[i];
                    key = key.StartsWith("-") ? key.Substring(1) : key;

                    if (key == "x" || key == "exclude")
                        excludes.Add(app_args[i + 1]);
                    else
                        if (i + 1 < app_args.Length)
                            args.Add(key, app_args[i + 1]);
                        else
                            args.Add(key, "");
                }
            }

            SetTitle();

            if (args.Count() > 0 && (args.ContainsKey("u") || args.ContainsKey("uninstall")))
                Uninstall();

            siger_backup_modified_bg = Registry.ClassesRoot.OpenSubKey($"{keyNameBg}SigerBackupModified", true);
            siger_backup_modified = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackupModified", true);
            siger_backup_bg = Registry.ClassesRoot.OpenSubKey($"{keyNameBg}SigerBackup", true);
            siger_backup = Registry.ClassesRoot.OpenSubKey($"{keyName}SigerBackup", true);

            if (siger_backup_bg == null || siger_backup_modified_bg == null || siger_backup == null || siger_backup_modified == null)
                Install();

            Run();
        }

        /// <summary>
        /// Obtém o diretório de saída.
        /// </summary>
        /// <returns></returns>
        public static string GetOutputDir()
        {
            if (args.ContainsKey("b") || args.ContainsKey("m") || args.ContainsKey("backup") || args.ContainsKey("modified"))
                return args.ContainsKey("b") ? args["b"] : args.ContainsKey("m") ? args["m"] : args.ContainsKey("backup") ? args["backup"] : args["modified"];

            return string.Empty;
        }

        /// <summary>
        /// Obtém a ação da aplicação (backup/modified).
        /// </summary>
        /// <returns></returns>
        public static string GetAction()
        {
            var isBackup = args.ContainsKey("b") || args.ContainsKey("backup");
            var isModified = args.ContainsKey("m") || args.ContainsKey("modified");

            if (isBackup || isModified)
                return isBackup ? "backup" : (isModified ? "modified" : string.Empty);

            return string.Empty;
        }

        /// <summary>
        /// Executa a plicação.
        /// </summary>
        public static void Run()
        {
            try
            {
                if (GetOutputDir() != null)
                    Directory.SetCurrentDirectory(GetOutputDir());
            }
            catch
            {
                Help();
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
                    SetTitle("Executando...");

                    switch (GetAction())
                    {
                        case "backup":
                            SetTitle("Efetuando backup completo");
                            Console.WriteLine("Efetuando backup...\n");
                            CompressFile("backup");
                            break;

                        case "modified":
                            SetTitle("Efetuando backup apenas modificados");
                            Console.WriteLine("Efetuando backup de arquivos modificados...\n");
                            CompressFile("modified");
                            break;

                        default:
                            Help();
                            break;
                    }

                    siger_backup_modified.Close();
                    siger_backup.Close();
                    ExitApp();
                }
                else
                    Install();

                if (args.Count < 2)
                    Environment.Exit(0);
            }
            catch { }
        }

        /// <summary>
        /// Atualiza o título da aplicação.
        /// </summary>
        /// <param name="title"></param>
        public static void SetTitle(string title = "")
        {
            Console.Title = "Siger Backup" + (title == "" ? "" : $": {title}");
        }

        /// <summary>
        /// Instala a aplicação.
        /// </summary>
        public static void Install()
        {
            if (!IsAdministrator())
            {
                SetTitle("Executando como Administrador...");
                Thread.Sleep(1);
                RunAsAdmin();
            }
            else
            {
                SetTitle("Instalando...");
                Console.ForegroundColor = ConsoleColor.DarkGreen;

                backup_dir = new FolderBrowserDialog
                {
                    Description = "Por favor, escolha o local onde irá salvar os backup's",
                    ShowNewFolderButton = false
                };

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
                            var key = MakeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            siger_backup_modified_bg = key["backup_bg"];
                            siger_backup_modified = key["backup"];
                        }

                        if (!cmd_backup.GetValue(null).ToString().Contains(App) || !cmd_backup_bg.GetValue(null).ToString().Contains(App))
                        {
                            var key = MakeKey("SigerBackup", "Siger Backup");
                            siger_backup_bg = key["backup_bg"];
                            siger_backup = key["backup"];
                        }

                        Console.WriteLine("Registro atualizado com êxito!");
                        PauseApp();
                    }
                }
                catch
                {
                    Console.WriteLine("Criando chaves no registro do Windows...");

                    if (backup_dir.ShowDialog() == DialogResult.OK)
                    {
                        if (siger_backup_modified == null || null == siger_backup_modified_bg)
                        {
                            var key = MakeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            siger_backup_modified_bg = key["backup_bg"];
                            siger_backup_modified = key["backup"];
                        }

                        if (siger_backup == null || null == siger_backup_bg)
                        {
                            var key = MakeKey("SigerBackup", "Siger Backup");
                            siger_backup_bg = key["backup_bg"];
                            siger_backup = key["backup"];
                        }

                        SetTitle("Instalação concluída!");
                        Console.Clear();
                        Log("Detalhes da instalação:", "Diretório de backup: ", backup_dir.SelectedPath);
                        Log("Menu de Contexto:", "Backup completo:|Backup apenas modificados:", " Siger Backup| Siger Backup [Modified] (git)");

                        PauseApp();
                    }
                }

                Run();
            }
        }

        /// <summary>
        /// Desinstala a aplicação.
        /// </summary>
        public static void Uninstall()
        {
            if (!IsAdministrator())
            {
                SetTitle("Executando como Administrador...");
                Thread.Sleep(1);
                RunAsAdmin();
            }
            else
            {
                SetTitle("Desinstalando...");

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

        /// <summary>
        /// Cria / atualiza as chaves de registro do Windows.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="title"></param>
        /// <returns></returns>
        public static Dictionary<string, RegistryKey> MakeKey(string name, string title)
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

            cmd_bg.SetValue("", $"\"{App}\" --{param} \"%V\" -x obj");
            cmd.SetValue("", $"\"{App}\" --{param} \"%V\" -x obj");

            var dict = new Dictionary<string, RegistryKey>
            {
                ["backup_bg"] = key_bg,
                ["backup"] = key
            };

            return dict;
        }

        /// <summary>
        /// Executa a aplicação como Administrador.
        /// </summary>
        public static void RunAsAdmin()
        {
            SetTitle("Executando como Administrador...");

            Thread.Sleep(1);

            ProcessStartInfo process = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Arguments = args.ContainsKey("u") || args.ContainsKey("uninstall") ? "--uninstall" : "",
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

        /// <summary>
        /// Verifica se a aplicação está sendo executada como Administrador.
        /// </summary>
        /// <returns></returns>
        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Faz backup completo ou apenas arquivos modificados.
        /// </summary>
        /// <param name="cmd"></param>
        private static void CompressFile(string cmd)
        {
            string output = string.Empty;
            string modified = string.Empty;
            List<string> allow_list = new List<string>();
            string filename = Path.Combine(siger_backup.GetValue("Backup Dir").ToString(), Path.GetFileName($"{GetOutputDir()}{GetVersion()}_{timestamp}.zip"));

            if (cmd == "backup")
            {
                string[] skip = new[] { ".rar", ".env", @"\.git\", ".gitignore", ".gitattributes", $"{Path.GetFileName(GetOutputDir())}.lst" };

                allow_list = Directory.GetFiles(GetOutputDir(), "*", SearchOption.AllDirectories).ToList();

                foreach (string s in skip.Concat(excludes))
                    allow_list = allow_list.Where(a => !a.Contains(s)).ToList();
            }
            else if (cmd == "modified")
            {
                modified = " (modified)";
                output += RunCommand("git.exe", "diff --name-only --diff-filter=M");
                output += RunCommand("git.exe", "ls-files -o --exclude-standard");

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
                    var zip = new ZipArchive(filename)
                    {
                        CompressionMethod = CompressionMethod.EnhancedDeflate,
                        CompressionLevel = 9
                    };

                    foreach (string f in allow_list)
                    {
                        if (File.Exists(f))
                        {
                            string file = f.Replace($@"{GetOutputDir()}\", "").Replace("\\", "/");
                            string file_path = Path.GetDirectoryName(file) != "" ? Path.GetDirectoryName(file) : "/";

                            Console.WriteLine($"Compactando arquivo {file}");

                            zip.Add(file, file_path);
                            total++;
                        }
                    }

                    zip.Close();

                    Console.ForegroundColor = ConsoleColor.Green;

                    if (total > 1)
                        Console.WriteLine($"\n  Foram feito backup{modified} de {total} arquivos.");
                    else
                        Console.WriteLine($"\n  Foi feito backup{modified} de um arquivo.");
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

                ExitApp();
            }
        }

        /// <summary>
        /// Executa um programa/comando.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        static string RunCommand(string file, string args)
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
                ExitApp();
            }

            process.WaitForExit();

            return output;
        }

        /// <summary>
        /// Obtém a versão da aplicação.
        /// </summary>
        /// <returns></returns>
        public static string GetVersion()
        {
            string version = string.Empty;
            string fileversion = string.Empty;

            foreach(var key in new string[] { "SIGER.php", "APP.php", "SGA.php" })
            {
                if (File.Exists($@"{GetOutputDir()}\core\{key}"))
                {
                    fileversion = $@"{GetOutputDir()}\core\{key}";
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

        /// <summary>
        /// Log personalizado.
        /// </summary>
        /// <param name="title"></param>
        /// <param name="_keys"></param>
        /// <param name="_values"></param>
        /// <param name="_desc"></param>
        public static void Log(string title, string _keys, string _values, string _desc = "")
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

        /// <summary>
        /// Mostra a ajuda da aplicação.
        /// </summary>
        public static void Help()
        {
            SetTitle("Como usar..");

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Faz backup (.zip) de uma pasta para o local escolhido na instalação.\n");

            Log("Opções:",
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
            Console.WriteLine($"\"{AppDomain.CurrentDomain.BaseDirectory}\"");
            Console.WriteLine();

            ExitApp();
        }

        /// <summary>
        /// Continua a aplicação após pressionar qualquer tecla.
        /// </summary>
        public static void PauseApp()
        {
            ShowVersion("Pressione qualquer tecla para continuar...");
        }

        /// <summary>
        /// Sai da aplicação após pressionar qualquer tecla.
        /// </summary>
        public static void ExitApp()
        {
            ShowVersion("Pressione qualquer tecla para sair...");
            Environment.Exit(0);
        }

        /// <summary>
        /// Mostra um texto (opcional) com a versão do App.
        /// </summary>
        /// <param name="txt"></param>
        public static void ShowVersion(string txt = "")
        {
            var v = $"v{version}";

            Console.ResetColor();
            Console.WriteLine();
            Console.Write(txt);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.CursorLeft = Console.BufferWidth - (v.Length + 2);
            Console.Write(v);
            Console.WriteLine();
            Console.ReadKey(true);
        }
    }
}