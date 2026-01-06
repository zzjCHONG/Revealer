using System.Text;

namespace ConsoleApp2
{
    //录像、下载xml文档、getvision无法使用

    class Program
    {
        // 错误码定义（参考SDK）
        const int SC_OK = 0;

        static void Main(string[] args)
        {
            Test_5_1_4();
            //Test_5_5_DataStream();
            //Test_5_6_AttributeOps();
            //Test_5_7_TriggerControl();

            //Test_5_8_OtherFunctions();
        }

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

        static void Test_5_5_DataStream()
        {
            Console.WriteLine("╔═══════════════════════════════════════════════╗");
            Console.WriteLine("║  测试 5.5：数据流操作                         ║");
            Console.WriteLine("╚═══════════════════════════════════════════════╝\n");

            int ret = SC_OK;
            IntPtr devHandle = IntPtr.Zero;

            try
            {
                // ============================================
                // 初始化并打开相机（复用5.1-5.4的流程）
                // ============================================
                Console.WriteLine("【准备阶段】初始化并打开相机");

                // 初始化SDK
                ret = NativeMethods.Camera_Initialize(3, ".", 10485760, 10);
                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 初始化失败! 错误码: {ret}");
                    return;
                }
                Console.WriteLine("  ✅ SDK已初始化");

                // 枚举设备
                int deviceCount = 0;
                ret = NativeMethods.Camera_EnumDevices(out deviceCount, 0);
                if (ret != SC_OK || deviceCount < 1)
                {
                    Console.WriteLine($"  ❌ 未找到设备");
                    return;
                }
                Console.WriteLine($"  ✅ 找到 {deviceCount} 个设备");

                // 创建句柄并打开
                int selectedIndex = 1;//第一个设备为虚拟设备
                ret = NativeMethods.Camera_CreateHandle(out devHandle, selectedIndex);
                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 创建句柄失败! 错误码: {ret}");
                    return;
                }

                ret = NativeMethods.Camera_Open(devHandle);
                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 打开相机失败! 错误码: {ret}");
                    return;
                }
                Console.WriteLine("  ✅ 相机已打开\n");

                // ============================================
                // 5.5.4 设置帧缓冲区数量
                // ============================================
                Console.WriteLine("【5.5.4】设置帧缓冲区");
                uint bufferCount = 5;
                ret = NativeMethods.Camera_SetBufferCount(devHandle, bufferCount);
                Console.WriteLine($"  缓冲区数量: {bufferCount}");
                Console.WriteLine($"  {(ret == SC_OK ? "✅" : "⚠️")} 设置结果: {ret}\n");

                // ============================================
                // 5.5.1 开始采集
                // ============================================
                Console.WriteLine("【5.5.1】开始采集");
                ret = NativeMethods.Camera_StartGrabbing(devHandle);

                if (ret != SC_OK)
                {
                    Console.WriteLine($"  ❌ 开始采集失败! 错误码: {ret}");
                    return;
                }
                Console.WriteLine("  ✅ 采集已开始\n");

                // ============================================
                // 5.5.3 检查采集状态
                // ============================================
                Console.WriteLine("【5.5.3】检查采集状态");
                int isGrabbing = 0;
                ret = NativeMethods.Camera_IsGrabbing(devHandle, out isGrabbing);
                Console.WriteLine($"  正在采集: {(isGrabbing == 1 ? "是" : "否")}");
                Console.WriteLine($"  {(ret == SC_OK ? "✅" : "⚠️")} 查询结果: {ret}\n");

                // ============================================
                // 5.5.5 获取图像帧
                // ============================================
                Console.WriteLine("【5.5.5】获取图像帧（连续采集5帧）");
                Console.WriteLine("----------------------------------------");

                int successCount = 0;
                for (int i = 0; i < 5; i++)
                {
                    NativeMethods.ImageData image = new NativeMethods.ImageData();
                    uint timeout = 5000;  // 5秒超时

                    Console.Write($"  帧 [{i + 1}] 获取中... ");

                    ret = NativeMethods.Camera_GetFrame(devHandle, ref image, timeout);

                    if (ret == SC_OK)
                    {
                        Console.WriteLine("✅ 成功");
                        Console.WriteLine($"        尺寸: {image.width}x{image.height}");
                        Console.WriteLine($"        格式: {image.pixelFormat}");
                        Console.WriteLine($"        大小: {image.dataSize} 字节");
                        Console.WriteLine($"        帧ID: {image.blockId}");
                        Console.WriteLine($"        时间戳: {image.timeStamp}");

                        // ============================================
                        // 5.5.6 释放帧资源
                        // ============================================
                        ret = NativeMethods.Camera_ReleaseFrame(devHandle, ref image);
                        if (ret == SC_OK)
                        {
                            Console.WriteLine($"        ✅ 已释放");
                            successCount++;
                        }
                        else
                        {
                            Console.WriteLine($"        ⚠️  释放失败 (错误码: {ret})");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ 失败 (错误码: {ret})");
                    }

                    Console.WriteLine();

                    // 短暂延迟
                    Thread.Sleep(100);
                }

                Console.WriteLine($"成功采集: {successCount}/5 帧\n");

                // ============================================
                // 5.5.7 获取处理后的图像（可选）
                // ============================================
                Console.WriteLine("【5.5.7】获取处理后的图像");
                NativeMethods.ImageData processedImage = new NativeMethods.ImageData();
                ret = NativeMethods.Camera_GetProcessedFrame(devHandle, ref processedImage, 5000);

                if (ret == SC_OK)
                {
                    Console.WriteLine($"  ✅ 成功获取处理后图像");
                    Console.WriteLine($"     尺寸: {processedImage.width}x{processedImage.height}");
                    NativeMethods.Camera_ReleaseFrame(devHandle, ref processedImage);
                }
                else
                {
                    Console.WriteLine($"  ⚠️  获取失败 (错误码: {ret})");
                }
                Console.WriteLine();

                //// ============================================
                //// 5.5.8-5.5.9 录像功能测试（可选）
                //// ============================================
                //Console.WriteLine("【5.5.8】测试录像功能（录制2秒）");

                //NativeMethods.RecordParam recordParam = new NativeMethods.RecordParam
                //{
                //    fileName = "test_video.avi",
                //    recordFormat = 0,  // AVI
                //    quality = 90,
                //    frameRate = 30
                //};

                //ret = NativeMethods.Camera_OpenRecord(devHandle, ref recordParam);

                //if (ret == SC_OK)
                //{
                //    Console.WriteLine($"  ✅ 录像已开始: {recordParam.fileName}");
                //    Console.WriteLine($"     格式: {(recordParam.recordFormat == 0 ? "AVI" : "MP4")}");
                //    Console.WriteLine($"     质量: {recordParam.quality}");
                //    Console.WriteLine($"     帧率: {recordParam.frameRate} fps");
                //    Console.WriteLine("     录制中...");

                //    // 录制2秒
                //    Thread.Sleep(2000);

                //    // 停止录像
                //    ret = NativeMethods.Camera_CloseRecord(devHandle);
                //    Console.WriteLine($"  {(ret == SC_OK ? "✅" : "⚠️")} 录像已停止\n");
                //}
                //else
                //{
                //    Console.WriteLine($"  ⚠️  开启录像失败 (错误码: {ret})\n");
                //}

                // ============================================
                // 5.5.2 停止采集
                // ============================================
                Console.WriteLine("【5.5.2】停止采集");
                ret = NativeMethods.Camera_StopGrabbing(devHandle);
                Console.WriteLine($"  {(ret == SC_OK ? "✅" : "⚠️")} 采集已停止 (结果: {ret})\n");

                // ============================================
                // 清理
                // ============================================
                Console.WriteLine("【清理阶段】关闭相机");
                NativeMethods.Camera_Close(devHandle);
                NativeMethods.Camera_DestroyHandle(devHandle);
                devHandle = IntPtr.Zero;
                Console.WriteLine("  ✅ 相机已关闭\n");

                Console.WriteLine("╔═══════════════════════════════════════════════╗");
                Console.WriteLine("║  ✅ 5.5 数据流测试完成                        ║");
                Console.WriteLine("╚═══════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 发生错误: {ex.Message}");
                Console.WriteLine($"详细信息: {ex}");
            }
            finally
            {
                // 清理资源
                if (devHandle != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.Camera_StopGrabbing(devHandle);
                        NativeMethods.Camera_Close(devHandle);
                        NativeMethods.Camera_DestroyHandle(devHandle);
                    }
                    catch { }
                }

                try
                {
                    NativeMethods.Camera_Release();
                }
                catch { }
            }
        }

        static void Test_5_6_AttributeOps()
        {
            Console.WriteLine("╔═══════════════════════════════════════════════╗");
            Console.WriteLine("║  5.6 属性操作测试                             ║");
            Console.WriteLine("╚═══════════════════════════════════════════════╝\n");

            IntPtr handle = IntPtr.Zero;

            try
            {
                // 准备：初始化并打开相机
                if (!InitAndOpenCamera(out handle)) return;

                // 5.6.1 检查属性可用性
                Console.WriteLine("【5.6.1】FeatureIsAvailable/Readable/Writeable");
                string featureName = "Width";
                int available = NativeMethods.Camera_FeatureIsAvailable(handle, featureName);
                int readable = NativeMethods.Camera_FeatureIsReadable(handle, featureName);
                int writeable = NativeMethods.Camera_FeatureIsWriteable(handle, featureName);
                Console.WriteLine($"  属性: '{featureName}'");
                Console.WriteLine($"  可用: {available}, 可读: {readable}, 可写: {writeable}");
                Console.WriteLine($"  {((available == 1) ? "✅" : "❌")} \n");

                // 5.6.2 获取属性类型
                Console.WriteLine("【5.6.2】Camera_GetFeatureType");
                int featureType = 0;
                int ret = NativeMethods.Camera_GetFeatureType(handle, featureName, out featureType);
                Console.WriteLine($"  属性: '{featureName}'");
                Console.WriteLine($"  返回: {ret}");
                Console.WriteLine($"  类型: {featureType} (0=Int, 1=Float, 2=Enum, 3=Bool, 4=String, 5=Cmd)");
                Console.WriteLine($"  {(ret == SC_OK ? "✅" : "❌")} \n");

                // 5.6.3 整型属性操作
                Console.WriteLine("【5.6.3】Integer属性操作 (Width)");
                long value = 0, minVal = 0, maxVal = 0, incVal = 0;
                ret = NativeMethods.Camera_GetIntFeatureValue(handle, "Width", out value);
                Console.WriteLine($"  当前值: {value} (ret={ret})");

                if (ret == SC_OK)
                {
                    NativeMethods.Camera_GetIntFeatureMin(handle, "Width", out minVal);
                    NativeMethods.Camera_GetIntFeatureMax(handle, "Width", out maxVal);
                    NativeMethods.Camera_GetIntFeatureInc(handle, "Width", out incVal);
                    Console.WriteLine($"  范围: [{minVal}, {maxVal}], 步进: {incVal}");

                    // 尝试设置
                    long newValue = value;
                    ret = NativeMethods.Camera_SetIntFeatureValue(handle, "Width", newValue);
                    Console.WriteLine($"  设置 {newValue}: {(ret == SC_OK ? "✅" : "❌")}");
                }
                Console.WriteLine();

                // 5.6.4 浮点属性操作
                Console.WriteLine("【5.6.4】Float属性操作 (ExposureTime)");
                double expTime = 0;
                ret = NativeMethods.Camera_GetFloatFeatureValue(handle, "ExposureTime", out expTime);
                Console.WriteLine($"  当前曝光: {expTime} us (ret={ret})");

                if (ret == SC_OK)
                {
                    double minExp = 0, maxExp = 0;
                    NativeMethods.Camera_GetFloatFeatureMin(handle, "ExposureTime", out minExp);
                    NativeMethods.Camera_GetFloatFeatureMax(handle, "ExposureTime", out maxExp);
                    Console.WriteLine($"  范围: [{minExp}, {maxExp}]");

                    ret = NativeMethods.Camera_SetFloatFeatureValue(handle, "ExposureTime", expTime);
                    Console.WriteLine($"  设置 {expTime}: {(ret == SC_OK ? "✅" : "❌")}");
                }
                Console.WriteLine();

                // 5.6.5 枚举属性操作
                Console.WriteLine("【5.6.5】Enum属性操作 (PixelFormat)");
                ulong enumValue = 0;
                ret = NativeMethods.Camera_GetEnumFeatureValue(handle, "PixelFormat", out enumValue);
                Console.WriteLine($"  当前值: {enumValue} (ret={ret})");

                if (ret == SC_OK)
                {
                    StringBuilder symbol = new StringBuilder(256);
                    NativeMethods.Camera_GetEnumFeatureSymbol(handle, "PixelFormat", symbol, 256);
                    Console.WriteLine($"  符号: {symbol}");

                    uint entryNum = 0;
                    NativeMethods.Camera_GetEnumFeatureEntryNum(handle, "PixelFormat", out entryNum);
                    Console.WriteLine($"  可选数: {entryNum}");
                    Console.WriteLine("  ✅");
                }
                Console.WriteLine();

                // 5.6.6 布尔属性操作
                Console.WriteLine("【5.6.6】Bool属性操作 (FanSwitch)");
                int boolValue = 0;
                ret = NativeMethods.Camera_GetBoolFeatureValue(handle, "FanSwitch", out boolValue);
                Console.WriteLine($"  当前值: {boolValue} (ret={ret})");
                if (ret == SC_OK)
                {
                    ret = NativeMethods.Camera_SetBoolFeatureValue(handle, "FanSwitch", boolValue);
                    Console.WriteLine($"  设置: {(ret == SC_OK ? "✅" : "❌")}");
                }
                Console.WriteLine();

                // 5.6.7 字符串属性操作
                Console.WriteLine("【5.6.7】String属性操作 (DeviceSerialNumber)");
                StringBuilder strValue = new StringBuilder(256);
                ret = NativeMethods.Camera_GetStringFeatureValue(handle, "DeviceSerialNumber", strValue, 256);
                Console.WriteLine($"  值: '{strValue}' (ret={ret})");
                Console.WriteLine($"  {(ret == SC_OK ? "✅" : "❌")} \n");

                // 5.6.8 命令属性操作
                Console.WriteLine("【5.6.8】Command属性操作 (TriggerSoftware)");
                Console.WriteLine("  (跳过执行，避免触发)");
                Console.WriteLine("  ⚠️  \n");

                Console.WriteLine("╔═══════════════════════════════════════════════╗");
                Console.WriteLine("║  ✅ 5.6 测试完成                              ║");
                Console.WriteLine("╚═══════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 错误: {ex.Message}");
            }
            finally
            {
                Cleanup(handle);
            }
        }

        #region 5.7 触发控制

        static void Test_5_7_TriggerControl()
        {
            Console.WriteLine("╔═══════════════════════════════════════════════╗");
            Console.WriteLine("║  5.7 触发控制测试                             ║");
            Console.WriteLine("╚═══════════════════════════════════════════════╝\n");

            IntPtr handle = IntPtr.Zero;

            try
            {
                // 准备：初始化并打开相机
                if (!InitAndOpenCamera(out handle)) return;

                //// 触发输入类型
                //Console.WriteLine("【5.7.1】触发输入控制");
                //ulong triggerInType = 0;
                //int ret = NativeMethods.Camera_GetTriggerInType(handle, out triggerInType);
                //Console.WriteLine($"  当前触发类型: {triggerInType} (ret={ret})");

                //if (ret == SC_OK)
                //{
                //    Console.WriteLine("  设置为软件触发 (5)");
                //    ret = NativeMethods.Camera_SetTriggerInType(handle, 5);
                //    Console.WriteLine($"  设置结果: {(ret == SC_OK ? "✅" : "❌")}");
                //}
                //Console.WriteLine();

                //// 触发激活方式
                //Console.WriteLine("【5.7.2】触发激活方式");
                //ulong activation = 0;
                //ret = NativeMethods.Camera_GetTriggerActivation(handle, out activation);
                //Console.WriteLine($"  当前激活: {activation} (0=上升沿, 1=下降沿)");
                //Console.WriteLine($"  {(ret == SC_OK ? "✅" : "❌")} \n");

                //// 触发延迟
                //Console.WriteLine("【5.7.3】触发延迟");
                //double delay = 0;
                //ret = NativeMethods.Camera_GetTriggerDelay(handle, out delay);
                //Console.WriteLine($"  当前延迟: {delay} us (ret={ret})");

                //if (ret == SC_OK)
                //{
                //    ret = NativeMethods.Camera_SetTriggerDelay(handle, 100.0);
                //    Console.WriteLine($"  设置100us: {(ret == SC_OK ? "✅" : "❌")}");
                //}
                //Console.WriteLine();

                //// 软件触发
                //Console.WriteLine("【5.7.4】软件触发");
                //Console.WriteLine("  (跳过执行SoftwareTrigger，避免意外触发)");
                //Console.WriteLine("  ⚠️  \n");

                //// 触发输出控制
                //Console.WriteLine("【5.7.5】触发输出控制");
                //ulong selector = 1;
                //ret = NativeMethods.Camera_SetTriggerOutSelector(handle, selector);
                //Console.WriteLine($"  选择输出端口: {selector} (ret={ret})");

                //if (ret == SC_OK)
                //{
                //    ulong outType = 0;
                //    NativeMethods.Camera_GetTriggerOutType(handle, out outType);
                //    Console.WriteLine($"  输出类型: {outType}");
                //    Console.WriteLine("  ✅");
                //}
                //Console.WriteLine();

                Console.WriteLine("╔═══════════════════════════════════════════════╗");
                Console.WriteLine("║  ✅ 5.7 测试完成                              ║");
                Console.WriteLine("╚═══════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 错误: {ex.Message}");
            }
            finally
            {
                Cleanup(handle);
            }
        }

        #endregion

        #region 5.8 其他功能

        static void Test_5_8_OtherFunctions()
        {
            Console.WriteLine("╔═══════════════════════════════════════════════╗");
            Console.WriteLine("║  5.8 其他功能测试                             ║");
            Console.WriteLine("╚═══════════════════════════════════════════════╝\n");

            IntPtr handle = IntPtr.Zero;

            try
            {
                // 准备：初始化并打开相机
                if (!InitAndOpenCamera(out handle)) return;

                // 自动曝光
                Console.WriteLine("【5.8.1】自动曝光");
                int ret = NativeMethods.Camera_SetAutoExposureParam(handle, 2, -1);  // 中央模式
                Console.WriteLine($"  设置参数 (mode=2, gray=-1): {(ret == SC_OK ? "✅" : "❌")}");
                Console.WriteLine("  (跳过执行AutoExposure)\n");

                // 自动色阶
                Console.WriteLine("【5.8.2】自动色阶");
                ret = NativeMethods.Camera_SetAutoLevels(handle, 1);  // 右色阶
                Console.WriteLine($"  设置模式 (1=右): {(ret == SC_OK ? "✅" : "❌")}");

                int mode = 0;
                NativeMethods.Camera_GetAutoLevels(handle, out mode);
                Console.WriteLine($"  读取模式: {mode}");

                int levelValue = 0;
                NativeMethods.Camera_GetAutoLevelValue(handle, 1, out levelValue);
                Console.WriteLine($"  右色阶值: {levelValue}");
                Console.WriteLine();

                // 图像处理功能
                Console.WriteLine("【5.8.3】图像处理功能");
                ret = NativeMethods.Camera_SetImageProcessingEnabled(handle, 0, 1);  // 启用亮度
                Console.WriteLine($"  启用亮度: {(ret == SC_OK ? "✅" : "❌")}");

                int enable = 0;
                NativeMethods.Camera_GetImageProcessingEnabled(handle, 0, out enable);
                Console.WriteLine($"  亮度状态: {enable}");

                ret = NativeMethods.Camera_SetImageProcessingValue(handle, 0, 60);  // 设置亮度值
                Console.WriteLine($"  设置亮度60: {(ret == SC_OK ? "✅" : "❌")}");

                int imgValue = 0;
                NativeMethods.Camera_GetImageProcessingValue(handle, 0, out imgValue);
                Console.WriteLine($"  读取亮度: {imgValue}");
                Console.WriteLine();

                // 伪彩映射
                Console.WriteLine("【5.8.4】伪彩映射");
                ret = NativeMethods.Camera_SetPseudoColorMap(handle, 1);  // Jet
                Console.WriteLine($"  设置Jet模式: {(ret == SC_OK ? "✅" : "❌")} \n");

                //// 便捷属性接口
                //Console.WriteLine("【5.8.5】便捷属性接口");
                //int width = 0, height = 0;
                //NativeMethods.Camera_GetWidth(handle, out width);
                //NativeMethods.Camera_GetHeight(handle, out height);
                //Console.WriteLine($"  图像尺寸: {width}x{height}");

                //double expTime = 0;
                //NativeMethods.Camera_GetExposureTime(handle, out expTime);
                //Console.WriteLine($"  曝光时间: {expTime} us");

                //float temp = 0;
                //ret = NativeMethods.Camera_GetDeviceTemperature(handle, out temp);
                //if (ret == SC_OK)
                //{
                //    Console.WriteLine($"  设备温度: {temp} °C");
                //}
                Console.WriteLine("  ✅\n");

                Console.WriteLine("╔═══════════════════════════════════════════════╗");
                Console.WriteLine("║  ✅ 5.8 测试完成                              ║");
                Console.WriteLine("╚═══════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 错误: {ex.Message}");
            }
            finally
            {
                Cleanup(handle);
            }
        }

        #endregion

        #region 辅助函数

        /// <summary>初始化SDK并创建句柄</summary>
        static bool InitAndCreateHandle(out IntPtr handle)
        {
            handle = IntPtr.Zero;

            // 初始化
            int ret = NativeMethods.Camera_Initialize(3, ".", 10485760, 10);
            if (ret != SC_OK)
            {
                Console.WriteLine($"❌ 初始化失败 (ret={ret})");
                return false;
            }

            // 枚举设备
            int deviceCount = 0;
            ret = NativeMethods.Camera_EnumDevices(out deviceCount, 0);
            if (ret != SC_OK || deviceCount < 1)
            {
                Console.WriteLine($"❌ 未找到设备");
                return false;
            }

            // 创建句柄
            ret = NativeMethods.Camera_CreateHandle(out handle, 1);
            if (ret != SC_OK || handle == IntPtr.Zero)
            {
                Console.WriteLine($"❌ 创建句柄失败 (ret={ret})");
                return false;
            }

            Console.WriteLine("✅ 准备完成\n");
            return true;
        }

        /// <summary>初始化SDK、创建句柄并打开相机</summary>
        static bool InitAndOpenCamera(out IntPtr handle)
        {
            if (!InitAndCreateHandle(out handle))
                return false;

            int ret = NativeMethods.Camera_Open(handle);
            if (ret != SC_OK)
            {
                Console.WriteLine($"❌ 打开相机失败 (ret={ret})");
                NativeMethods.Camera_DestroyHandle(handle);
                NativeMethods.Camera_Release();
                handle = IntPtr.Zero;
                return false;
            }

            return true;
        }

        /// <summary>清理资源</summary>
        static void Cleanup(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.Camera_Close(handle);
                    NativeMethods.Camera_DestroyHandle(handle);
                }
                catch { }
            }

            try
            {
                NativeMethods.Camera_Release();
            }
            catch { }
        }

        #endregion
    }
}



