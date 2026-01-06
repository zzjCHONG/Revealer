using EyeCam.Shared.Native;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AllocConsole();

            //Test_5_1_4();
        }

        // 错误码定义（参考SDK）
        const int SC_OK = 0;

        static void Test_5_1_4()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  Revealer SDK 测试程序");
            Console.WriteLine("========================================\n");

            int ret = SC_OK;
            IntPtr devHandle = IntPtr.Zero;

            try
            {
                //// ============================================
                //// 1. 获取SDK版本
                //// ============================================
                //Console.WriteLine("【步骤1】获取SDK版本");
                //string version = NativeMethods.GetVersionString();
                //Console.WriteLine($"  SDK版本: {version}");

                //if (string.IsNullOrEmpty(version))
                //{
                //    Console.WriteLine("  ❌ 错误: 无法获取SDK版本，可能DLL未正确加载");
                //    Console.WriteLine("  请检查：");
                //    Console.WriteLine("    1. RevealerDemo.dll 是否在程序目录");
                //    Console.WriteLine("    2. 依赖的 SCApi.dll 等文件是否存在");
                //    return;
                //}
                //Console.WriteLine("  ✅ 成功\n");

                // ============================================
                // 2. 初始化SDK
                // ============================================
                Console.WriteLine("【步骤2】初始化SDK");
                ret = NativeMethods.Camera_Initialize(
                    2,              // Info级别日志
                    ".",            // 当前目录
                    10 * 1024 * 1024,  // 10MB日志文件
                    1);            // 10个日志文件

                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 初始化失败! 错误码: {ret}");
                    return;
                }
                Console.WriteLine("  ✅ 初始化成功\n");

                // ============================================
                // 3. 枚举设备
                // ============================================
                Console.WriteLine("【步骤3】枚举设备");
                int deviceCount = 0;
                ret = NativeMethods.Camera_EnumDevices(out deviceCount, 0);  // 0 = eInterfaceTypeAll

                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 枚举设备失败! 错误码: {ret}");
                    Console.WriteLine("  可能原因：");
                    Console.WriteLine("    1. 相机未连接");
                    Console.WriteLine("    2. USB驱动未安装");
                    Console.WriteLine("    3. 相机被其他程序占用");
                    return;
                }

                Console.WriteLine($"  找到 {deviceCount} 个设备");

                if (deviceCount < 1)
                {
                    Console.WriteLine("  ❌ 未找到相机设备");
                    Console.WriteLine("  请检查：");
                    Console.WriteLine("    1. 相机是否正确连接（USB/CXP）");
                    Console.WriteLine("    2. 设备管理器中是否识别");
                    Console.WriteLine("    3. 驱动是否正确安装");
                    return;
                }

                // ============================================
                // 4. 显示设备信息
                // ============================================
                Console.WriteLine("\n【步骤4】设备列表");
                Console.WriteLine("索引  设备名称");
                Console.WriteLine("----------------------------------------");

                for (int i = 0; i < deviceCount; i++)
                {
                    StringBuilder name = new StringBuilder(256);
                    ret = NativeMethods.Camera_GetDeviceName(i, name, 256);

                    if (ret == SC_OK)
                    {
                        Console.WriteLine($"  [{i}]  {name}");
                    }
                    else
                    {
                        Console.WriteLine($"  [{i}]  获取名称失败 (错误码: {ret})");
                    }
                }
                Console.WriteLine();

                // ============================================
                // 5. 选择设备（使用第一个设备）
                // ============================================
                int selectedIndex = 1;//第一个设备为虚拟设备
                Console.WriteLine($"【步骤5】选择设备");
                Console.WriteLine($"  使用设备索引: {selectedIndex}");

                // ============================================
                // 6. 创建设备句柄
                // ============================================
                Console.WriteLine("\n【步骤6】创建设备句柄");
                ret = NativeMethods.Camera_CreateHandle(out devHandle, selectedIndex);

                if (ret != SC_OK || devHandle == IntPtr.Zero)
                {
                    Console.WriteLine($"  ❌ 创建句柄失败! 错误码: {ret}");
                    Console.WriteLine("  可能原因：");
                    Console.WriteLine("    1. 设备索引无效");
                    Console.WriteLine("    2. 设备已被打开");
                    Console.WriteLine("    3. 内部错误");
                    return;
                }
                Console.WriteLine($"  ✅ 句柄创建成功: 0x{devHandle.ToString("X")}");

                // ============================================
                // 7. 打开相机
                // ============================================
                Console.WriteLine("\n【步骤7】打开相机");
                ret = NativeMethods.Camera_Open(devHandle);

                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 打开相机失败! 错误码: {ret}");
                    Console.WriteLine("  可能原因：");
                    Console.WriteLine("    1. 相机已被其他程序打开");
                    Console.WriteLine("    2. USB连接不稳定");
                    Console.WriteLine("    3. 权限不足");
                    Console.WriteLine("    4. 设备断开连接");

                    // 清理句柄后退出
                    NativeMethods.Camera_DestroyHandle(devHandle);
                    return;
                }
                Console.WriteLine("  ✅ 相机打开成功");

                // ============================================
                // 8. 获取设备详细信息
                // ============================================
                Console.WriteLine("\n【步骤8】获取设备信息");
                NativeMethods.DeviceInfo info = new NativeMethods.DeviceInfo();
                ret = NativeMethods.Camera_GetDeviceInfo(devHandle, ref info);

                if (ret == SC_OK)
                {
                    Console.WriteLine("  设备详细信息：");
                    Console.WriteLine($"    相机名称: {info.cameraName}");
                    Console.WriteLine($"    序列号:   {info.serialNumber}");
                    Console.WriteLine($"    型号:     {info.modelName}");
                    Console.WriteLine($"    制造商:   {info.manufacturerInfo}");
                    Console.WriteLine($"    版本:     {info.deviceVersion}");
                    Console.WriteLine("  ✅ 成功");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  获取设备信息失败 (错误码: {ret})");
                }

                //// ============================================
                //// 9. 下载XML配置（可选）
                //// ============================================
                //Console.WriteLine("\n【步骤9】下载XML配置（可选）");
                //string xmlPath = "camera_config.xml";
                //ret = NativeMethods.Camera_DownloadGenICamXML(devHandle, xmlPath);

                //if (ret == SC_OK)
                //{
                //    Console.WriteLine($"  ✅ XML配置已保存到: {xmlPath}");
                //}
                //else
                //{
                //    Console.WriteLine($"  ⚠️  下载XML失败 (错误码: {ret})");
                //}

                var res = NativeMethods.Camera_GetIntFeatureValue(devHandle, "Width", out var value);

                // ============================================
                // 10. 关闭相机
                // ============================================
                Console.WriteLine("\n【步骤10】关闭相机");
                ret = NativeMethods.Camera_Close(devHandle);

                if (ret == SC_OK)
                {
                    Console.WriteLine("  ✅ 相机关闭成功");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  关闭相机失败 (错误码: {ret})");
                }

                // ============================================
                // 11. 销毁句柄
                // ============================================
                Console.WriteLine("\n【步骤11】销毁句柄");
                ret = NativeMethods.Camera_DestroyHandle(devHandle);
                devHandle = IntPtr.Zero;

                if (ret == SC_OK)
                {
                    Console.WriteLine("  ✅ 句柄销毁成功");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  销毁句柄失败 (错误码: {ret})");
                }

                // ============================================
                // 完成
                // ============================================
                Console.WriteLine("\n========================================");
                Console.WriteLine("  ✅ 测试完成！所有步骤执行成功");
                Console.WriteLine("========================================");
            }
            catch (DllNotFoundException ex)
            {
                Console.WriteLine($"\n❌ DLL加载错误: {ex.Message}");
                Console.WriteLine("\n请检查以下文件是否存在：");
                Console.WriteLine("  1. RevealerDemo.dll");
                Console.WriteLine("  2. SCApi.dll");
                Console.WriteLine("  3. 其他依赖的DLL文件");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 发生错误: {ex.Message}");
                Console.WriteLine($"\n详细信息:\n{ex}");
            }
            finally
            {
                // ============================================
                // 清理资源
                // ============================================
                if (devHandle != IntPtr.Zero)
                {
                    Console.WriteLine("\n执行清理...");
                    try
                    {
                        NativeMethods.Camera_Close(devHandle);
                        NativeMethods.Camera_DestroyHandle(devHandle);
                    }
                    catch
                    {
                        // 忽略清理错误
                    }
                }

                // 释放SDK
                try
                {
                    NativeMethods.Camera_Release();
                    Console.WriteLine("SDK资源已释放");
                }
                catch
                {
                    // 忽略释放错误
                }
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }

}
