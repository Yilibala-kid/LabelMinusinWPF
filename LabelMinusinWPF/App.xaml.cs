using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using LabelMinusinWPF.SelfControls;

namespace LabelMinusinWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 加载标签样式设置
            LabelStyleManager.Instance.LoadSettings();

            var mainWindow = new MainWindow();

            // 检查命令行参数
            if (e.Args.Length > 0)
            {
                string firstArg = e.Args[0];
                bool isReviewMode = e.Args.Length > 1 && e.Args[0] == "--review";

                if (isReviewMode)
                {
                    // 图校模式：打开第二个参数指定的文件
                    string filePath = e.Args[1];
                    mainWindow.Show();
                    mainWindow.OpenImageReviewWithFile(filePath);
                }
                else
                {
                    // 普通模式：打开文件
                    mainWindow.Show();
                    mainWindow.OpenFileOnStartup(firstArg);
                }
            }
            else
            {
                // 无参数：正常启动
                mainWindow.Show();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 保存标签样式设置
            LabelStyleManager.Instance.SaveSettings();
            base.OnExit(e);
        }
    }
}
