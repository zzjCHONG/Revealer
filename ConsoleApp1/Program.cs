using System;
using System.Collections.Generic;
using System.Threading;
using EyeCam.Shared;
using EyeCam.Shared.Models;
using EyeCam.Shared.Enums;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== EyeCamera SDK 接口验证 Demo ===");

            try
            {
                // 1. [SC_Init] 初始化 SDK
                Console.WriteLine("\n[1] 正在初始化 SDK...");
                EyeCamera.Initialize(logLevel: 3, logPath: "./logs");
                Console.WriteLine($"SDK 版本: {EyeCamera.GetVersion()}");

                // 2. [SC_EnumDevices] 枚举设备
                Console.WriteLine("\n[2] 正在搜索设备...");
                List<CameraInfo> devices = EyeCamera.EnumerateDevices();

                if (devices.Count == 0)
                {
                    Console.WriteLine("未找到相机设备，程序退出。");
                    return;
                }

                foreach (var dev in devices)
                {
                    Console.WriteLine($"发现设备: Index={dev.Index}, Name={dev.Name}");
                }

                // 选择第一个设备进行测试
                int targetIndex = devices[1].Index;

                // 3. [SC_CreateHandle] 创建设备句柄 (通过构造函数)
                using (EyeCamera camera = new EyeCamera(targetIndex))
                {
                    Console.WriteLine($"\n[3] 已为设备 {targetIndex} 创建句柄");

                    // 4. [SC_Open] 打开设备
                    Console.WriteLine("[4] 正在打开设备...");
                    camera.Open();

                    // --- 验证参数设置与获取 ---
                    VerifyCameraParameters(camera);

                    // --- 验证录像功能 (可选) ---
                    // VerifyRecording(camera);

                    // 5. [SC_StartGrabbing] 开始采集
                    Console.WriteLine("\n[5] 开始采集图像...");
                    camera.StartGrabbing();

                    // 6. [SC_GetFrame] 获取数据 (循环采集 5 帧进行验证)
                    Console.WriteLine("[6] 正在获取图像数据 (同步模式)...");
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            // 调用 GetFrame 接口
                            var frame = camera.GetFrame(timeout: 2000);
                            Console.WriteLine($"   帧 {i + 1}: 宽度={frame.Width}, 高度={frame.Height}, 序号={frame.BlockId}");

                            // 提示：ImageFrame 在构造时已包含数据，GetFrame 内部会自动处理 Camera_ReleaseFrame
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"   获取第 {i + 1} 帧失败: {ex.Message}");
                        }
                        Thread.Sleep(100);
                    }

                    // 7. [SC_StopGrabbing] 停止采集
                    Console.WriteLine("\n[7] 停止采集...");
                    camera.StopGrabbing();

                    // 8. [SC_Close] 关闭设备
                    Console.WriteLine("[8] 关闭设备连接...");
                    camera.Close();

                } // 9. [SC_DestroyHandle] 销毁句柄 (由 Dispose 自动触发)
                Console.WriteLine("\n[9] 设备句柄已销毁");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n发生异常: {ex.Message}");
            }
            finally
            {
                // 10. [SC_Release] 释放 SDK
                Console.WriteLine("\n[10] 正在释放 SDK 资源...");
                EyeCamera.Release();
                Console.WriteLine("Demo 运行结束。");
            }

            Console.ReadLine();
        }

        /// <summary>
        /// 验证常用参数接口
        /// </summary>
        static void VerifyCameraParameters(EyeCamera camera)
        {
            Console.WriteLine("\n--- 验证参数接口 ---");

            // 获取设备信息
            var info = camera.GetDeviceInfo();
            Console.WriteLine($"设备型号: {info.ModelName}, 序列号: {info.SerialNumber}");

            // 温度
            Console.WriteLine($"当前温度: {camera.Temperature}°C");

            // 设置曝光 (微秒)
            camera.ExposureTime = 10000;
            Console.WriteLine($"设置曝光为: {camera.ExposureTime} us");

            // 设置 ROI (示例：设置宽度一半)
            int maxWidth = camera.SensorWidth;
            int maxHeight = camera.SensorHeight;
            camera.SetROI(maxWidth / 2, maxHeight / 2, 0, 0);
            Console.WriteLine($"设置 ROI 为: {camera.Width}x{camera.Height}");

            // 图像处理验证 (例如伪彩)
            camera.SetImageProcessingEnabled(ImageProcessingFeature.PseudoColor, true);
            camera.SetPseudoColorMap(PseudoColorMap.Jet);
            Console.WriteLine("已开启伪彩处理 (Jet)");
        }

        /// <summary>
        /// 验证录像接口
        /// </summary>
        static void VerifyRecording(EyeCamera camera)
        {
            Console.WriteLine("\n--- 验证录像接口 ---");
            string videoPath = "test_video.mp4";
            camera.StartRecording(videoPath, recordFormat: 1, quality: 90, frameRate: 30);
            Console.WriteLine($"录像已开始: {videoPath}");

            Thread.Sleep(3000); // 录制 3 秒

            camera.StopRecording();
            Console.WriteLine("录像已停止。");
        }
    }
}