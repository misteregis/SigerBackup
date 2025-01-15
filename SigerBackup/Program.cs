using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Policy;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using Ionic.Zip;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using SigerBackup.Classes;
using static System.Windows.Forms.Design.AxImporter;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace SigerBackup
{
    internal class Program
    {
        private static readonly string App = Assembly.GetEntryAssembly()?.Location;
        private static readonly string Timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
        private static readonly string Version = Application.ProductVersion;
        private static readonly Dictionary<string, string> Args = new Dictionary<string, string>();
        private static readonly List<string> Excludes = new List<string>();
        private static VistaFolderBrowserDialog _backupDir;
        private static RegistryKey _sigerBackupModifiedPublishBg;
        private static RegistryKey _sigerBackupModifiedPublish;
        private static RegistryKey _sigerBackupModifiedBg;
        private static RegistryKey _sigerBackupModified;
        private static RegistryKey _sigerBackupBg;
        private static RegistryKey _sigerBackup;
        private static bool _isFTP = false;
        private static string _arg;

        private const string KeyNameBg = @"Directory\Background\shell\";
        private const string KeyName = @"Directory\shell\";

        private static IniFile _ini;

        [STAThread]
        private static void Main(string[] appArgs)
        {
            for (var i = 0; i < appArgs.Length; i += 2)
            {
                if (!appArgs[i].StartsWith("-")) continue;

                var key = appArgs[i].StartsWith("--") ? appArgs[i].Substring(2) : appArgs[i];
                key = key.StartsWith("-") ? key.Substring(1) : key;

                if (key == "x" || key == "exclude")
                {
                    Excludes.Add(appArgs[i + 1]);
                }
                else
                {
                    if (i + 1 < appArgs.Length)
                    {
                        Args.Add(key, appArgs[i + 1]);
                        _arg += $"{appArgs[i]} \"{appArgs[i + 1]}\"";
                    }
                    else
                        Args.Add(key, "");
                }
            }

            SetTitle();

            if (Args.Count > 0 && (Args.ContainsKey("u") || Args.ContainsKey("uninstall")))
                Uninstall();

            _sigerBackupModifiedPublishBg = Registry.ClassesRoot.OpenSubKey($"{KeyNameBg}SigerBackupModifiedPublish", true);
            _sigerBackupModifiedPublish = Registry.ClassesRoot.OpenSubKey($"{KeyName}SigerBackupModifiedPublish", true);
            _sigerBackupModifiedBg = Registry.ClassesRoot.OpenSubKey($"{KeyNameBg}SigerBackupModified", true);
            _sigerBackupModified = Registry.ClassesRoot.OpenSubKey($"{KeyName}SigerBackupModified", true);
            _sigerBackupBg = Registry.ClassesRoot.OpenSubKey($"{KeyNameBg}SigerBackup", true);
            _sigerBackup = Registry.ClassesRoot.OpenSubKey($"{KeyName}SigerBackup", true);

            CheckAppPath();
            Run();
        }

        /// <summary>
        /// Obtém o diretório de saída.
        /// </summary>
        /// <returns></returns>
        public static string GetOutputDir()
        {
            if (Args.ContainsKey("b") || Args.ContainsKey("m") || Args.ContainsKey("p") || Args.ContainsKey("backup") || Args.ContainsKey("modified") || Args.ContainsKey("publish"))
                return Args.ContainsKey("b") ? Args["b"] :
                    Args.ContainsKey("m") ? Args["m"] :
                    Args.ContainsKey("p") ? Args["p"] :
                    Args.ContainsKey("backup") ? Args["backup"] :
                    Args.ContainsKey("publish") ? Args["publish"] :
                    Args["modified"];

            return string.Empty;
        }

        /// <summary>
        /// Obtém a ação da aplicação (backup/modified).
        /// </summary>
        /// <returns></returns>
        public static string GetAction()
        {
            var isBackup = Args.ContainsKey("b") || Args.ContainsKey("backup");
            var isModified = Args.ContainsKey("m") || Args.ContainsKey("modified");
            var isPublish = Args.ContainsKey("p") || Args.ContainsKey("publish");

            if (isBackup || isModified || isPublish)
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                return isBackup ? "backup" : isModified ? "modified" : isPublish ? "publish" : string.Empty;

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

            string action = GetAction();

            if (action == "publish")
            {
                var publishFile = Path.Combine(GetOutputDir(), "publish.siger");

                _ini = new IniFile(publishFile);

                if (!File.Exists(publishFile))
                {
                    var exeFile = new OpenFileDialog
                    {
                        Title = "Selecione o executável",
                        InitialDirectory = GetOutputDir(),
                        Filter = "C# Executável (*.exe)|*.exe"
                    };

                    var xmlFile = new OpenFileDialog
                    {
                        Title = "Selecione o arquivo de configuração (.xml)",
                        RestoreDirectory = true,
                        Filter = "Arquivo XML (*.xml)|*.xml"
                    };

                    if (exeFile.ShowDialog() == DialogResult.OK)
                        _ini.Write("Executable", FileVersionInfo.GetVersionInfo(exeFile.FileName).InternalName, "publish");
                    else
                        ExitApp(publishFile);

                    if (xmlFile.ShowDialog() == DialogResult.OK)
                        _ini.Write("XMLFile", xmlFile.FileName, "publish");
                    else
                        ExitApp(publishFile);
                }
            }

            try
            {
                Console.Clear();

                if (CheckAppPath())
                {
                    SetTitle("Executando...");

                    switch (action)
                    {
                        case "backup":
                            SetTitle("Efetuando backup completo");
                            Console.WriteLine(" Efetuando backup...\n");
                            break;

                        case "modified":
                            SetTitle("Efetuando backup apenas modificados");
                            Console.WriteLine(" Efetuando backup de arquivos modificados...\n");
                            break;

                        case "publish":
                            SetTitle("Efetuando backup (publish) apenas modificados");
                            Console.WriteLine(" Efetuando backup de arquivos modificados (publish)...\n");
                            break;

                        default:
                            Help();
                            break;
                    }

                    var allowedActions = new List<String> { "backup", "modified", "publish" };

                    if (allowedActions.Any(key => key == action))
                    {
                        CompressFile(action);
                    }

                    CloseRegistry();
                    ExitApp();
                }
                else
                    Install();

                if (Args.Count < 2)
                    Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Exception(ex);
            }
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

                if (RunCommand("git.exe", "config --global core.safecrlf").Split('\n').First() != "false")
                    if (MessageBox.Show("Deseja desativar o aviso de LF/CLRL (core.safecrlf)?", "Configuração do Git",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        RunCommand("git.exe", "config --global core.safecrlf false");

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                _backupDir = new VistaFolderBrowserDialog
                {
                    Description = "Por favor, escolha o local onde irá salvar os backup's",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                try
                {
                    var cmdBackupModifiedPublishBg = _sigerBackupModifiedPublishBg.OpenSubKey("command");
                    var cmdBackupModifiedPublish = _sigerBackupModifiedPublish.OpenSubKey("command");
                    var cmdBackupModifiedBg = _sigerBackupModifiedBg.OpenSubKey("command");
                    var cmdBackupModified = _sigerBackupModified.OpenSubKey("command");
                    var cmdBackupBg = _sigerBackupBg.OpenSubKey("command");
                    var cmdBackup = _sigerBackup.OpenSubKey("command");

                    if (cmdBackup != null && cmdBackupModified != null && cmdBackupModifiedPublish != null && null != cmdBackupBg && null != cmdBackupModifiedBg && null != cmdBackupModifiedPublishBg)
                    {
                        Console.WriteLine(" Atualizando chaves no registro do Windows...");

                        if (!cmdBackupModified.GetValue(null).ToString().Contains(App) || !cmdBackupModifiedBg.GetValue(null).ToString().Contains(App))
                        {
                            var key = MakeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            _sigerBackupModifiedBg = key["backup_bg"];
                            _sigerBackupModified = key["backup"];
                        }

                        if (!cmdBackup.GetValue(null).ToString().Contains(App) || !cmdBackupBg.GetValue(null).ToString().Contains(App))
                        {
                            var key = MakeKey("SigerBackup", "Siger Backup");
                            _sigerBackupBg = key["backup_bg"];
                            _sigerBackup = key["backup"];
                        }

                        if (!cmdBackupModifiedPublish.GetValue(null).ToString().Contains(App) || !cmdBackupModifiedPublishBg.GetValue(null).ToString().Contains(App))
                        {
                            var key = MakeKey("SigerBackupModifiedPublish", "Siger Backup Publish [Modified] (git)");
                            _sigerBackupModifiedPublishBg = key["backup_bg"];
                            _sigerBackupModifiedPublish = key["backup"];
                        }

                        Console.WriteLine(" Registro atualizado com êxito!");
                        PauseApp();
                    }
                }
                catch
                {
                    Console.WriteLine(" Criando chaves no registro do Windows...");

                    // ReSharper disable once PossibleInvalidOperationException
                    if ((bool)_backupDir.ShowDialog())
                    {
                        if (_sigerBackupModified == null || null == _sigerBackupModifiedBg)
                        {
                            var key = MakeKey("SigerBackupModified", "Siger Backup [Modified] (git)");
                            _sigerBackupModifiedBg = key["backup_bg"];
                            _sigerBackupModified = key["backup"];
                        }

                        if (_sigerBackup == null || null == _sigerBackupBg)
                        {
                            var key = MakeKey("SigerBackup", "Siger Backup");
                            _sigerBackupBg = key["backup_bg"];
                            _sigerBackup = key["backup"];
                        }

                        if (_sigerBackupModifiedPublish == null || null == _sigerBackupModifiedPublishBg)
                        {
                            var key = MakeKey("SigerBackupModifiedPublish", "Siger Backup Publish [Modified] (git)");
                            _sigerBackupModifiedPublishBg = key["backup_bg"];
                            _sigerBackupModifiedPublish = key["backup"];
                        }

                        SetTitle("Instalação concluída!");
                        Console.Clear();
                        Log("Detalhes da instalação:", "Diretório de backup: ", _backupDir.SelectedPath);
                        Log("Menu de Contexto:", "Backup completo:|Backup apenas modificados:|Backup apenas modificados (publish):", " Siger Backup| Siger Backup [Modified] (git)| Siger Backup Publish [Modified] (git)");

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

                var mainBg = Registry.ClassesRoot.OpenSubKey(KeyNameBg, true);
                var main = Registry.ClassesRoot.OpenSubKey(KeyName, true);

                if (mainBg != null)
                {
                    mainBg.DeleteSubKeyTree("SigerBackupModifiedPublish", false);
                    mainBg.DeleteSubKeyTree("SigerBackupModified", false);
                    mainBg.DeleteSubKeyTree("SigerBackup", false);
                    mainBg.Close();
                }

                if (main != null)
                {
                    main.DeleteSubKeyTree("SigerBackupModifiedPublish", false);
                    main.DeleteSubKeyTree("SigerBackupModified", false);
                    main.DeleteSubKeyTree("SigerBackup", false);
                    main.Close();
                }

                Console.WriteLine(" Desinstalado com sucesso!");
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
            var param = name == "SigerBackup" ? "backup" : name == "SigerBackupModified" ? "modified" : "publish";

            var keyBg = Registry.ClassesRoot.CreateSubKey($"{KeyNameBg}{name}");
            if (keyBg != null)
            {
                var cmdBg = keyBg.CreateSubKey("command");

                if (_backupDir.SelectedPath != string.Empty)
                    keyBg.SetValue("Backup Dir", _backupDir.SelectedPath, RegistryValueKind.String);

                keyBg.SetValue("MUIVerb", $"{title}", RegistryValueKind.String);
                keyBg.SetValue("Icon", App, RegistryValueKind.String);

                cmdBg?.SetValue("", $"\"{App}\" --{param} \"%V\" -x obj");
            }

            var key = Registry.ClassesRoot.CreateSubKey($"{KeyName}{name}");

            if (key != null)
            {
                var cmd = key.CreateSubKey("command");

                if (_backupDir.SelectedPath != string.Empty)
                    key.SetValue("Backup Dir", _backupDir.SelectedPath, RegistryValueKind.String);

                key.SetValue("MUIVerb", $"{title}", RegistryValueKind.String);
                key.SetValue("Icon", App, RegistryValueKind.String);

                cmd?.SetValue("", $"\"{App}\" --{param} \"%V\" -x obj");
            }

            var dict = new Dictionary<string, RegistryKey>
            {
                ["backup_bg"] = keyBg,
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

            var process = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Arguments = Args.ContainsKey("u") || Args.ContainsKey("uninstall") ? "--uninstall" : _arg,
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
            var output = string.Empty;
            var zipName = string.Empty;
            var modified = string.Empty;
            var allowList = new List<string>();
            var filename = Path.Combine(_sigerBackup.GetValue("Backup Dir").ToString(), Path.GetFileName($"{GetOutputDir()}{GetVersion()}_{Timestamp}.zip"));

            FileVersionInfo versionInfo = null;

            switch (cmd)
            {
                case "backup":
                    var skip = new[] { ".rar", ".env", @"\.git\", ".gitignore", ".gitattributes", ".gitkeep", $"{Path.GetFileName(GetOutputDir())}.lst" };

                    allowList = Directory.GetFiles(GetOutputDir(), "*", SearchOption.AllDirectories).ToList();

                    allowList = skip.Concat(Excludes).Aggregate(allowList, (current, s) => current.Where(a => !a.Contains(s)).ToList());

                    break;

                case "modified":
                case "publish":
                    RunCommand("git.exe", "init");

                    modified = " (modified)";
                    output += RunCommand("git.exe", "diff --name-only --diff-filter=M");
                    output += RunCommand("git.exe", "ls-files -o --exclude-standard");

                    if (cmd == "publish")
                    {
                        modified = " (modified) [publish]";

                        var executable = _ini.Read("Executable", "publish");

                        if (!executable.Contains("\\"))
                        {
                            executable = $"{GetOutputDir()}\\{executable}";
                        }

                        versionInfo = FileVersionInfo.GetVersionInfo(executable);

                        if (!string.IsNullOrEmpty(RunCommand("git.exe", $"log --grep=v{versionInfo.ProductVersion}", false)))
                        {
                            Error($"A versão \"{versionInfo.ProductVersion}\" já foi processada!");
                            ExitApp();
                        }

                        var name = Path.GetFileNameWithoutExtension(versionInfo.InternalName);
                        var xmlFile = _ini.Read("XMLFile", "publish");

                        zipName = $"{name} Update v{versionInfo.ProductVersion}.zip".Replace(" ", "_");
                        filename = Path.Combine(Path.GetDirectoryName(xmlFile) ?? string.Empty, zipName);

                        if (IsValidUrl(xmlFile))
                        {
                            Uri uri = new Uri(xmlFile);

                            filename = xmlFile.Replace(Path.GetFileName(uri.LocalPath), zipName);

                            _isFTP = true;
                        }
                    }

                    allowList = output.Split(
                        new[] { "\r\n", "\r", "\n" },
                        StringSplitOptions.None
                    ).ToList();

                    break;
            }

            try
            {
                var total = 0;

                allowList.Sort();

                var runZip = allowList.Any(File.Exists);

                Console.ForegroundColor = ConsoleColor.DarkYellow;

                if (!_isFTP && File.Exists(filename))
                {
                    File.Delete(filename);
                }

                if (runZip)
                {
                    using (var zip = new ZipFile())
                    {
                        foreach (var f in allowList)
                        {
                            if (f.Contains("publish.siger") || !File.Exists(f)) continue;

                            var file = f.Replace($@"{GetOutputDir()}\", "").Replace("\\", "/");
                            var filePath = Path.GetDirectoryName(file) != "" ? Path.GetDirectoryName(file) : "/";

                            Console.WriteLine($"  Compactando arquivo {file}");

                            zip.AddFile(file, filePath);
                            total++;
                        }

                        if (_isFTP)
                        {
                            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

                            zip.Save(tempFile);

                            UploadFileToFtp(filename, tempFile);
                        }
                        else
                        {
                            zip.Save(filename);
                        }
                    }

                    if (cmd == "publish" && versionInfo != null)
                    {
                        RunCommand("git.exe", "add .");
                        RunCommand("git.exe", $"commit -m \"v{versionInfo.ProductVersion}\"");

                        var xmlFile = _ini.Read("XMLFile", "publish");
                        var xml = XElement.Load(xmlFile);

                        // ReSharper disable once PossibleNullReferenceException
                        xml.Element("version").Value = versionInfo.ProductVersion;
                        xml.Element("url").Value = filename;

                        if (_isFTP)
                        {
                            var tempXMLFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");

                            xml.Save(tempXMLFile);

                            UploadFileToFtp(xmlFile, tempXMLFile);
                        } else
                        {
                            xml.Save(xmlFile);
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"\nArquivo: ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(zipName);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(total > 1
                        ? $"\n  Foram feito backup{modified} de {total} arquivos."
                        : $"\n  Foi feito backup{modified} de um arquivo.");
                }
                else
                    Console.WriteLine($"  Não há arquivos para fazer backup{modified}.");
            }
            catch (Exception ex)
            {
                Exception(ex);
            }
        }

        /// <summary>
        /// Executa um programa/comando.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="args"></param>
        /// <param name="exit"></param>
        /// <returns></returns>
        static string RunCommand(string file, string args, bool exit = true)
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
            var output = process.StandardOutput.ReadToEnd();
            var err = process.StandardError.ReadToEnd();

            if (err != "" && exit)
            {
                CloseRegistry();
                Error(err);
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
            var version = string.Empty;
            var fileversion = string.Empty;

            foreach(var key in new[] { "SIGER.php", "APP.php", "SGA.php" })
            {
                if (!File.Exists($@"{GetOutputDir()}\core\{key}")) continue;

                fileversion = $@"{GetOutputDir()}\core\{key}";

                break;
            }

            if (fileversion == string.Empty) return version;

            var lines = File.ReadAllLines(fileversion);

            if (lines.Any(t => t.Contains("VERSION")))
                version = $"_v{lines.First(t => t.Contains("VERSION")).Split('"')[1]}";

            return version;
        }

        /// <summary>
        /// Log personalizado.
        /// </summary>
        /// <param name="title"></param>
        /// <param name="keys"></param>
        /// <param name="values"></param>
        /// <param name="desc"></param>
        public static void Log(string title, string keys, string values, string desc = "")
        {
            var splitDesc = desc.Split('|');
            var splitKeys = keys.Split('|');
            var splitValues = values.Split('|');

            Console.ResetColor();
            Console.WriteLine($" {title}{(title.GetLast(1) != ":" ? ":" : "")}");

            for (var i = 0; i < splitKeys.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write($"  {splitKeys[i]}");
                Console.ForegroundColor = ConsoleColor.DarkYellow;

                if (splitDesc.ElementAtOrDefault(i) != null)
                {
                    Console.Write($" {splitValues[i]}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($" {splitDesc[i]}");
                }
                else
                {
                    Console.WriteLine($" {splitValues[i]}");
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
            Console.WriteLine(" Faz backup (.zip) de uma pasta para o local escolhido na instalação.\n");

            Log("Opções:",
                "[-u, --uninstall]|[-m, --modified]|[-p, --publish]|[-b, --backup]",
                "\tDesinstala esta aplicação.|\tEfetua o backup dos arquivos modificados e/ou não rastreável.|\tEfetua o backup dos arquivos modificados (publish).|\tEfetua o backup completo.",
                "Remove as chaves do registro do Windows.|Apenas git!|Apenas git!"
            );

            Console.ResetColor();
            Console.WriteLine(" Exemplo:");
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
        public static void ExitApp(string publishFile = null)
        {
            if (publishFile != null) File.Delete(publishFile);

            ShowVersion("Pressione qualquer tecla para sair...");
            Environment.Exit(0);
        }

        /// <summary>
        /// Mostra um texto (opcional) com a versão do App.
        /// </summary>
        /// <param name="txt"></param>
        public static void ShowVersion(string txt = "")
        {
            var v = $"v{Version}";

            Console.ResetColor();
            Console.WriteLine();
            Console.Write($" {txt}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.CursorLeft = Console.BufferWidth - (v.Length + 2);
            Console.Write(v);
            Console.WriteLine();
            Console.ReadKey(true);
        }

        /// <summary>
        /// Fecha a chave do registro e a libera no disco se seu conteúdo tiver sido modificado.
        /// </summary>
        public static void CloseRegistry()
        {
            _sigerBackupModifiedPublishBg.Close();
            _sigerBackupModifiedPublish.Close();
            _sigerBackupModifiedBg.Close();
            _sigerBackupModified.Close();
            _sigerBackupBg.Close();
            _sigerBackup.Close();
        }

        public static void Error(string txt)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"  Erro: {txt}");
            Console.WriteLine();
            ExitApp();
        }

        public static bool CheckAppPath()
        {
            var notInstalled = _sigerBackup == null ||
                                _sigerBackupBg == null ||
                                _sigerBackupModified == null ||
                                _sigerBackupModifiedBg == null ||
                                _sigerBackupModifiedPublish == null ||
                                _sigerBackupModifiedPublishBg == null;

            if (notInstalled) Install();

            var cmdBackupModifiedPublishBg = _sigerBackupModifiedPublishBg?.OpenSubKey("command");
            var cmdBackupModifiedPublish = _sigerBackupModifiedPublish?.OpenSubKey("command");
            var cmdBackupModifiedBg = _sigerBackupModifiedBg?.OpenSubKey("command");
            var cmdBackupModified = _sigerBackupModified?.OpenSubKey("command");
            var cmdBackupBg = _sigerBackupBg?.OpenSubKey("command");
            var cmdBackup = _sigerBackup?.OpenSubKey("command");

            var run = (cmdBackupModifiedPublishBg != null && cmdBackupModifiedPublishBg.GetValue(null).ToString().Contains(App)) &&
                  (cmdBackupModifiedPublish != null && cmdBackupModifiedPublish.GetValue(null).ToString().Contains(App)) &&
                  (cmdBackupModifiedBg != null && cmdBackupModifiedBg.GetValue(null).ToString().Contains(App)) &&
                  (cmdBackupModified != null && cmdBackupModified.GetValue(null).ToString().Contains(App)) &&
                  (cmdBackupBg?.GetValue(null) != null && cmdBackupBg.GetValue(null).ToString().Contains(App)) &&
                  (cmdBackup?.GetValue(null) != null && cmdBackup.GetValue(null).ToString().Contains(App));

            if (!run) Install();

            return run;
        }

        public static void Exception(Exception ex)
        {
            CloseRegistry();

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;

#if (DEBUG)
            Console.WriteLine(ex);
#else
                Console.WriteLine("Ocorreu um erro:");
                Console.WriteLine(ex.Message);
#endif

            ExitApp();
        }

        /// <summary>
        /// Função para verificar se uma string começa com ftp://, http:// ou https://
        /// </summary>
        /// <param name="url"></param>
        /// <returns>Um boolean</returns>
        public static bool IsValidUrl(string url)
        {
            // Padrão de regex para encontrar strings iniciadas com ftp://, http:// ou https://
            string pattern = @"^(ftp|http|https)://";

            Regex regex = new Regex(pattern);

            return regex.IsMatch(url);
        }

        public static bool UploadFileToFtp(string remoteFile, string localFile, string username = null, string password = null)
        {
            var request = (FtpWebRequest)WebRequest.Create(remoteFile);

            if (username != null && null != password)
            {
                request.Credentials = new NetworkCredential(username, password);
            }

            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.UsePassive = true;
            request.UseBinary = true;
            request.KeepAlive = false;

            using (var fileStream = File.OpenRead(localFile))
            {
                using (var requestStream = request.GetRequestStream())
                {
                    fileStream.CopyTo(requestStream);
                    requestStream.Close();
                }
            }

            var response = (FtpWebResponse)request.GetResponse();

            response.Close();

            return (int) response.StatusCode == 200;
        }
    }
}