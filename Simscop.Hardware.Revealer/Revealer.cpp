#include "Revealer.h"
#include <SCApi.h>
#include <SCDefines.h>
#include <map>
#include <string.h>

// =================================================================
// 全局数据管理
// =================================================================

/// <summary>
/// 设备列表缓存
/// 用途：枚举设备后保存设备信息，供后续创建句柄使用
/// 生命周期：在Camera_EnumDevices调用时更新
/// </summary>
static SC_DeviceList g_deviceList = { 0 };

/// <summary>
/// 句柄映射表
/// 键：CameraHandle (void*) - 对外暴露的句柄
/// 值：SC_DEV_HANDLE - SDK内部句柄
/// 用途：将外部句柄映射到SDK句柄，实现句柄隔离
/// </summary>
static std::map<CameraHandle, SC_DEV_HANDLE> g_handleMap;

// =================================================================
// 回调函数管理
// =================================================================

/// <summary>
/// 回调信息结构
/// </summary>
struct CallbackInfo
{
    void* userCallback;  // 用户回调函数指针
    void* userData;      // 用户数据
};

/// <summary>
/// 连接状态回调映射表
/// </summary>
static std::map<CameraHandle, CallbackInfo> g_connectCallbackMap;

/// <summary>
/// 参数更新回调映射表
/// </summary>
static std::map<CameraHandle, CallbackInfo> g_paramUpdateCallbackMap;

/// <summary>
/// 导出状态回调映射表
/// </summary>
static std::map<CameraHandle, CallbackInfo> g_exportCallbackMap;

/// <summary>
/// 帧回调
/// </summary>
static std::map<CameraHandle, CallbackInfo> g_processedFrameCallbackMap;

// =================================================================
// 辅助函数
// =================================================================

/// <summary>
/// 从CameraHandle获取对应的SDK句柄
/// </summary>
/// <param name="handle">外部句柄</param>
/// <returns>对应的SDK句柄，未找到返回nullptr</returns>
static SC_DEV_HANDLE GetSDKHandle(CameraHandle handle)
{
    auto it = g_handleMap.find(handle);
    return (it != g_handleMap.end()) ? it->second : nullptr;
}
// =================================================================
// 静态回调包装函数
// =================================================================

/// <summary>
/// 连接状态回调包装函数
/// </summary>
static void SC_CALL OnConnectCallback(const SC_SConnectArg* pConnectArg, void* pUser)
{
    if (!pConnectArg) return;

    CameraHandle handle = reinterpret_cast<CameraHandle>(pUser);
    auto it = g_connectCallbackMap.find(handle);
    if (it != g_connectCallbackMap.end())
    {
        ConnectCallBack callback = reinterpret_cast<ConnectCallBack>(it->second.userCallback);
        if (callback)
        {
            // 根据SC_EVType判断连接状态
            // eOffLine = 0 (离线), eOnLine = 1 (在线)
            int isConnected = (pConnectArg->event == eOnLine) ? 1 : 0;

            // 使用序列号作为设备标识
            const char* deviceId = pConnectArg->serialNumber;

            callback(isConnected, deviceId, it->second.userData);
        }
    }
}

/// <summary>
/// 参数更新回调包装函数
/// </summary>
static void SC_CALL OnParamUpdateCallback(const SC_SParamUpdateArg* pParamUpdateArg, void* pUser)
{
    if (!pParamUpdateArg || !pParamUpdateArg->pParamNameList) return;

    CameraHandle handle = reinterpret_cast<CameraHandle>(pUser);
    auto it = g_paramUpdateCallbackMap.find(handle);
    if (it != g_paramUpdateCallbackMap.end())
    {
        ParamUpdateCallBack callback = reinterpret_cast<ParamUpdateCallBack>(it->second.userCallback);
        if (callback)
        {
            // 遍历所有受影响的参数，对每个参数调用一次用户回调
            for (unsigned int i = 0; i < pParamUpdateArg->nParamCnt; i++)
            {
                const char* paramName = pParamUpdateArg->pParamNameList[i].str;
                callback(paramName, it->second.userData);
            }
        }
    }
}

/// <summary>
/// 导出状态回调包装函数
/// SDK定义：ExportEventCB(int progress, const char *msgText, int notify, void* user)
/// </summary>
static void SC_CALL OnExportCallback(int progress, const char* msgText, int notify, void* pUser)
{
    CameraHandle handle = reinterpret_cast<CameraHandle>(pUser);
    auto it = g_exportCallbackMap.find(handle);
    if (it != g_exportCallbackMap.end())
    {
        ExportEventCallBack callback = reinterpret_cast<ExportEventCallBack>(it->second.userCallback);
        if (callback)
        {
            // 将SDK的notify映射为简化的status
            // ExportNotify枚举：eExportStart=0, eExportProcessing=1, eExportFinish=2, eExportClose=3
            int status = notify;  // 直接使用SDK的notify值

            callback(status, progress, it->second.userData);
        }
    }
}

static void SC_CALL OnProcessedFrameCallback(SC_Frame* pFrame, void* pUser)
{
    if (!pFrame) return;

    CameraHandle handle = reinterpret_cast<CameraHandle>(pUser);
    auto it = g_processedFrameCallbackMap.find(handle);
    if (it != g_processedFrameCallbackMap.end())
    {
        FrameCallBack callback = reinterpret_cast<FrameCallBack>(it->second.userCallback);
        if (callback)
        {
            // 转换 SC_Frame 到 ImageData
            ImageData imageData;
            imageData.width = pFrame->frameInfo.width;
            imageData.height = pFrame->frameInfo.height;
            imageData.pixelFormat = pFrame->frameInfo.pixelFormat;
            imageData.dataSize = pFrame->frameInfo.size;
            imageData.stride = pFrame->frameInfo.size / pFrame->frameInfo.height;
            imageData.blockId = pFrame->frameInfo.frameId;
            imageData.timeStamp = pFrame->frameInfo.timeStamp;
            imageData.pData = (unsigned char*)pFrame->pData;

            callback(&imageData, it->second.userData);
        }
    }
}

// =================================================================
// 5.1 系统操作
// =================================================================

/// <summary>
/// 获取SDK版本信息
/// </summary>
/// <returns>版本字符串，例如："1.1.6"</returns>
/// <remarks>
/// - 返回的字符串由SDK内部管理，不需要释放
/// - 版本格式：主版本.次版本.修订版本
/// </remarks>
REVEALER_API const char* Camera_GetVersion()
{
    return SC_GetVersion();
}

/// <summary>
/// 初始化SDK
/// 注意：必须在使用任何其他SDK功能之前调用
/// </summary>
/// <param name="logLevel">日志级别
///   0 = Off    - 关闭日志
///   1 = Error  - 仅错误
///   2 = Warn   - 警告及以上
///   3 = Info   - 信息及以上（推荐）
///   4 = Debug  - 调试及以上（详细）
/// </param>
/// <param name="logPath">日志保存路径，nullptr表示当前目录</param>
/// <param name="fileSize">单个日志文件大小（字节）
///   推荐值：10485760 (10MB)
/// </param>
/// <param name="fileNum">日志文件循环数量
///   推荐值：10
///   说明：达到数量后会覆盖最旧的文件
/// </param>
/// <returns>SC_OK(0)表示成功，其他值表示错误码</returns>
/// <remarks>
/// 使用示例：
/// Camera_Initialize(3, "C:/Logs", 10485760, 10);
/// 将在C:/Logs目录创建最多10个日志文件，每个10MB
/// </remarks>
REVEALER_API ErrorCode Camera_Initialize(int logLevel, const char* logPath,
    unsigned int fileSize, unsigned int fileNum)
{
    // 如果未指定路径，使用当前目录
    if (!logPath) logPath = ".";

    // 转换为SDK的日志级别枚举
    SCLogLevel level = static_cast<SCLogLevel>(logLevel);

    return SC_Init(level, logPath, fileSize, fileNum);
}

/// <summary>
/// 释放SDK资源
/// 注意：程序退出前必须调用，释放所有资源
/// </summary>
/// <remarks>
/// 调用顺序：
/// 1. 关闭所有相机 (Camera_Close)
/// 2. 销毁所有句柄 (Camera_DestroyHandle)
/// 3. 最后调用本函数
/// </remarks>
REVEALER_API void Camera_Release()
{
    // 清空句柄映射表
    g_handleMap.clear();

    // 清空回调映射表
    g_connectCallbackMap.clear();
    g_paramUpdateCallbackMap.clear();
    g_exportCallbackMap.clear();

    g_processedFrameCallbackMap.clear();

    // 释放SDK资源
    SC_Release();
}

/// <summary>
/// 枚举设备
/// </summary>
/// <param name="pDeviceCount">输出：找到的设备数量</param>
/// <param name="interfaceType">接口类型，控制枚举范围：
///   0 (eInterfaceTypeAll)    - 枚举所有接口类型的设备（推荐）
///   1 (eInterfaceTypeUsb3)   - 仅枚举USB3接口设备
///   2 (eInterfaceTypeCXP)    - 仅枚举CXP（CoaXPress）接口设备
///   3 (eInterfaceTypeCustom) - 仅枚举虚拟设备（用于测试）
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要说明：
/// 1. 枚举结果保存在全局g_deviceList中
/// 2. 每次调用会覆盖之前的枚举结果
/// 3. 必须在创建句柄之前调用
/// 4. 热插拔：设备状态改变后需要重新枚举
/// 
/// interfaceType选择建议：
/// - 通常使用 eInterfaceTypeAll (0)
/// - 如果明确知道设备类型，可指定具体类型提高枚举速度
/// - Gloria 4.2通常是USB3或CXP接口
/// 
/// 注意：SDK文档中有笔误：
/// - eInterfaceTypeCXP 应该枚举CXP设备
/// - eInterfaceTypeUsb3 应该枚举USB3设备
/// 实际使用时以设备实际接口类型为准
/// </remarks>
REVEALER_API ErrorCode Camera_EnumDevices(int* pDeviceCount, unsigned int interfaceType)
{
    if (!pDeviceCount) return -1;

    // 调用SDK枚举接口
    // 参数说明：
    // - &g_deviceList: 输出设备列表
    // - interfaceType: 接口类型（见上方注释）
    // - nullptr: cti路径，nullptr表示使用环境变量GENICAM_GENTL64_PATH
    int ret = SC_EnumDevices(&g_deviceList, interfaceType, nullptr);

    if (ret == SC_OK)
    {
        // 返回找到的设备数量
        *pDeviceCount = g_deviceList.devNum;
    }

    return ret;
}

/// <summary>
/// 获取指定索引设备的名称
/// 注意：必须先调用Camera_EnumDevices
/// </summary>
/// <param name="index">设备索引，范围 [0, deviceCount-1]</param>
/// <param name="name">输出：设备名称缓冲区</param>
/// <param name="nameSize">缓冲区大小，推荐256字节</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 设备名称示例："Gloria 4.2"
/// 使用示例：
/// char name[256];
/// Camera_GetDeviceName(0, name, sizeof(name));
/// </remarks>
REVEALER_API ErrorCode Camera_GetDeviceName(int index, char* name, int nameSize)
{
    if (!name || index < 0 || index >= g_deviceList.devNum)
        return -1;

    // 从枚举结果中复制设备名称
    strncpy_s(name, nameSize, g_deviceList.pDevInfo[index].cameraName, _TRUNCATE);
    return SC_OK;
}

/// <summary>
/// 创建设备句柄
/// 注意：必须先调用Camera_EnumDevices
/// </summary>
/// <param name="pHandle">输出：创建的设备句柄</param>
/// <param name="deviceIndex">设备索引，对应枚举时的索引</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 句柄生命周期：
/// 1. 创建句柄 (Camera_CreateHandle)
/// 2. 打开相机 (Camera_Open)
/// 3. 使用相机...
/// 4. 关闭相机 (Camera_Close)
/// 5. 销毁句柄 (Camera_DestroyHandle)
/// 
/// 注意：
/// - 一个设备只能创建一个句柄（Exclusive模式）
/// - 句柄创建后不会自动打开相机
/// - 必须配对调用DestroyHandle
/// </remarks>
REVEALER_API ErrorCode Camera_CreateHandle(CameraHandle* pHandle, int deviceIndex)
{
    if (!pHandle || deviceIndex < 0 || deviceIndex >= g_deviceList.devNum)
        return -1;

    SC_DEV_HANDLE sdkHandle = nullptr;

    // 使用CameraKey方式创建句柄（推荐）
    // 其他方式：
    // - eModeByIndex: 通过索引（不稳定，热插拔后可能变化）
    // - eModeBySerialNumber: 通过序列号（最稳定）
    // - eModeByCameraKey: 通过CameraKey（推荐，兼顾稳定性和便利性）
    int ret = SC_CreateHandle(&sdkHandle, eModeByCameraKey,
        g_deviceList.pDevInfo[deviceIndex].cameraKey);

    if (ret == SC_OK && sdkHandle != nullptr)
    {
        // 使用SDK句柄作为外部句柄
        CameraHandle handle = (CameraHandle)sdkHandle;

        // 保存映射关系
        g_handleMap[handle] = sdkHandle;

        // 返回外部句柄
        *pHandle = handle;
    }

    return ret;
}

/// <summary>
/// 销毁设备句柄，释放相关资源
/// 注意：销毁前必须先关闭相机
/// </summary>
/// <param name="handle">要销毁的句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
REVEALER_API ErrorCode Camera_DestroyHandle(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    // 销毁SDK句柄
    int ret = SC_DestroyHandle(sdkHandle);

    if (ret == SC_OK)
    {
        // 从映射表中移除
        g_handleMap.erase(handle);

        // 清理回调映射
        g_connectCallbackMap.erase(handle);
        g_paramUpdateCallbackMap.erase(handle);
        g_exportCallbackMap.erase(handle);

        g_processedFrameCallbackMap.erase(handle);
    }

    return ret;
}

// =================================================================
// 5.2 相机操作
// =================================================================

/// <summary>
/// 打开相机（默认权限）
/// 等同于 Camera_OpenEx(handle, AccessPermissionExclusive)
/// </summary>
/// <param name="handle">设备句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 默认使用独占模式（Exclusive），其他程序无法同时访问
/// </remarks>
REVEALER_API ErrorCode Camera_Open(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_Open(sdkHandle);
}

///// <summary>
///// 打开相机（指定访问权限）
///// </summary>
///// <param name="handle">设备句柄</param>
///// <param name="accessPermission">访问权限模式：
/////   0 (AccessPermissionUnknown)   - 未知权限
/////   1 (AccessPermissionNone)      - 无访问权限
/////   2 (AccessPermissionMonitor)   - 只读模式，多个程序可同时访问
/////   3 (AccessPermissionControl)   - 控制模式，可修改参数，独占访问
/////   4 (AccessPermissionExclusive) - 独占模式，完全控制（推荐）
///// </param>
///// <returns>SC_OK(0)表示成功</returns>
///// <remarks>
///// 权限说明：
///// - Monitor: 适合只读监控，不能采集图像
///// - Control: 可控制相机但独占访问
///// - Exclusive: 完全控制，推荐用于图像采集
///// 
///// 失败原因：
///// - 相机已被其他程序打开（Exclusive模式）
///// - USB连接问题
///// - 驱动未安装
///// </remarks>
//REVEALER_API ErrorCode Camera_OpenEx(CameraHandle handle, int accessPermission)
//{
//    //SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
//    //if (!sdkHandle) return -1;
//
//    //// 转换访问权限枚举
//    //SC_ECameraAccessPermission permission =
//    //    static_cast<SC_ECameraAccessPermission>(accessPermission);
//
//    //return SC_OpenEx(sdkHandle, permission);
//
//    return -1;
//}

/// <summary>
/// 关闭相机
/// 注意：关闭前必须先停止采集
/// </summary>
/// <param name="handle">设备句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 正确的关闭顺序：
/// 1. 停止采集 (Camera_StopGrabbing)
/// 2. 关闭相机 (Camera_Close)
/// 3. 销毁句柄 (Camera_DestroyHandle)
/// </remarks>
REVEALER_API ErrorCode Camera_Close(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_Close(sdkHandle);
}

// =================================================================
// 5.3 相机配置下载
// =================================================================

/// <summary>
/// 下载相机的GenICam XML配置文件
/// 用途：用于查看相机完整的属性定义和能力
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pFullPath">保存路径（含文件名）
///   例如："C:/Config/camera_config.xml"
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// XML文件包含：
/// - 所有可用属性及其类型、范围
/// - 属性之间的依赖关系
/// - 相机能力描述
/// 
/// 通常用于：
/// - 调试和开发
/// - 了解相机完整功能
/// - GenICam标准工具的配置
/// </remarks>
REVEALER_API ErrorCode Camera_DownloadGenICamXML(CameraHandle handle, const char* pFullPath)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pFullPath) return -1;

    return SC_DownLoadGenICamXML(sdkHandle, pFullPath);
}

// =================================================================
// 5.4 获取设备信息
// =================================================================

/// <summary>
/// 获取设备详细信息
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pDevInfo">输出：设备信息结构</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 返回信息包括：
/// - cameraName: 设备名称，如"Gloria 4.2"
/// - serialNumber: 序列号（唯一标识）
/// - modelName: 型号名称
/// - manufacturerName: 制造商，如"ZKJD"
/// - deviceVersion: 固件版本
/// </remarks>
REVEALER_API ErrorCode Camera_GetDeviceInfo(CameraHandle handle, DeviceInfo* pDevInfo)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pDevInfo) return -1;

    SC_DeviceInfo info;
    int ret = SC_GetDeviceInfo(sdkHandle, &info);

    if (ret == SC_OK)
    {
        // 复制各个字段
        strncpy_s(pDevInfo->cameraName, sizeof(pDevInfo->cameraName),
            info.cameraName, _TRUNCATE);
        strncpy_s(pDevInfo->serialNumber, sizeof(pDevInfo->serialNumber),
            info.serialNumber, _TRUNCATE);
        strncpy_s(pDevInfo->modelName, sizeof(pDevInfo->modelName),
            info.modelName, _TRUNCATE);
        strncpy_s(pDevInfo->manufacturerInfo, sizeof(pDevInfo->manufacturerInfo),
            info.manufactureInfo, _TRUNCATE);
        strncpy_s(pDevInfo->deviceVersion, sizeof(pDevInfo->deviceVersion),
            info.deviceVersion, _TRUNCATE);
    }

    return ret;
}

// =================================================================
// 5.5 相机数据流操作
// =================================================================

/// <summary>
/// 开始采集图像
/// </summary>
/// <param name="handle">设备句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 采集流程：
/// 1. 配置相机参数（曝光、ROI等）
/// 2. 调用Camera_StartGrabbing开始采集
/// 3. 调用Camera_GetFrame获取图像
/// 4. 处理图像
/// 5. 调用Camera_ReleaseFrame释放图像
/// 6. 重复3-5
/// 7. 调用Camera_StopGrabbing停止采集
/// 
/// 注意：
/// - 开始采集前必须打开相机
/// - 必须配对调用StopGrabbing
/// - 采集期间可以修改部分参数（如曝光时间）
/// </remarks>
REVEALER_API ErrorCode Camera_StartGrabbing(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_StartGrabbing(sdkHandle);
}

/// <summary>
/// 停止采集图像
/// </summary>
/// <param name="handle">设备句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 停止后：
/// - 不再产生新的图像
/// - 缓冲区中的图像仍可获取
/// - 可以修改所有参数
/// - 可以重新StartGrabbing
/// </remarks>
REVEALER_API ErrorCode Camera_StopGrabbing(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_StopGrabbing(sdkHandle);
}

/// <summary>
/// 检查是否正在采集
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pIsGrabbing">输出：1=正在采集，0=未采集</param>
/// <returns>SC_OK(0)表示成功</returns>
REVEALER_API ErrorCode Camera_IsGrabbing(CameraHandle handle, int* pIsGrabbing)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pIsGrabbing) return -1;

    // SC_IsGrabbing返回bool，转换为int
    *pIsGrabbing = SC_IsGrabbing(sdkHandle) ? 1 : 0;
    return SC_OK;
}

/// <summary>
/// 设置帧缓冲区数量
/// 建议在StartGrabbing之前调用
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="bufferCount">缓冲区数量
///   推荐值：3-10
///   过小：可能丢帧
///   过大：占用内存多，增加延迟
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 缓冲区机制：
/// - SDK维护一个图像缓冲队列
/// - 相机采集的图像先放入队列
/// - GetFrame从队列头部取图像
/// - 队列满时，新图像会丢弃（丢帧）
/// 
/// 建议值：
/// - 实时预览：3-5个
/// - 录像/保存：5-10个
/// - 高速采集：10-20个
/// </remarks>
REVEALER_API ErrorCode Camera_SetBufferCount(CameraHandle handle, unsigned int bufferCount)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_SetBufferCount(sdkHandle, bufferCount);
}

/// <summary>
/// 获取一帧图像（同步方式）
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pImage">输出：图像数据结构</param>
/// <param name="timeout">超时时间（毫秒）
///   0xFFFFFFFF = 无限等待
///   0 = 立即返回
///   其他值 = 等待指定毫秒数
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要：
/// 1. pImage->pData指向SDK内部内存
/// 2. C#端必须立即复制数据
/// 3. 处理完后必须调用Camera_ReleaseFrame释放
/// 4. 不释放会导致内存泄漏和缓冲区耗尽
/// 
/// 图像数据结构：
/// - width, height: 图像尺寸
/// - stride: 每行字节数（可能!=width*pixelSize）
/// - pixelFormat: 像素格式（见枚举定义）
/// - pData: 图像数据指针
/// - dataSize: 数据总大小
/// - blockId: 帧序号（从0开始递增）
/// - timeStamp: 时间戳（相机内部时钟）
/// 
/// 超时处理：
/// - 超时返回错误码
/// - 通常原因：相机未触发、帧率太低、硬件问题
/// </remarks>
REVEALER_API ErrorCode Camera_GetFrame(CameraHandle handle, ImageData* pImage, unsigned int timeout)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pImage) return -1;

    SC_Frame frame;
    frame.pData = nullptr;

    // 获取一帧图像
    int ret = SC_GetFrame(sdkHandle, &frame, timeout);
    if (ret != SC_OK) return ret;

    // 验证图像数据有效性
    if (frame.frameInfo.width == 0 || frame.frameInfo.height == 0 ||
        frame.frameInfo.size == 0 || !frame.pData)
    {
        // 数据无效，释放并返回错误
        SC_ReleaseFrame(sdkHandle, &frame);
        return -1;
    }

    // 填充ImageData结构
    pImage->width = frame.frameInfo.width;
    pImage->height = frame.frameInfo.height;
    pImage->pixelFormat = frame.frameInfo.pixelFormat;
    pImage->dataSize = frame.frameInfo.size;
    pImage->stride = frame.frameInfo.size / frame.frameInfo.height;
    pImage->blockId = frame.frameInfo.frameId;      // 帧序号
    pImage->timeStamp = frame.frameInfo.timeStamp;  // 时间戳
    pImage->pData = (unsigned char*)frame.pData;    // 注意：指向SDK内存

    return SC_OK;
}

/// <summary>
/// 释放帧资源
/// 必须与Camera_GetFrame配对调用
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pImage">要释放的图像</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 释放机制：
/// - 将缓冲区归还给SDK
/// - 缓冲区可以被重用
/// - 未释放会导致缓冲区耗尽
/// 
/// 注意：
/// - 释放后pData指针失效
/// - 不要重复释放同一帧
/// </remarks>
REVEALER_API ErrorCode Camera_ReleaseFrame(CameraHandle handle, ImageData* pImage)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pImage) return -1;

    // 构造SC_Frame用于释放
    SC_Frame frame;
    frame.pData = pImage->pData;
    frame.frameInfo.width = pImage->width;
    frame.frameInfo.height = pImage->height;
    frame.frameInfo.pixelFormat = (SC_EPixelType)pImage->pixelFormat;
    frame.frameInfo.size = pImage->dataSize;

    return SC_ReleaseFrame(sdkHandle, &frame);
}

/// <summary>
/// 获取处理后的图像（同步方式）
/// 用途：获取经过SDK图像处理后的图像
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="pImage">输出：处理后的图像数据</param>
/// <param name="timeout">超时时间（毫秒）</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 图像处理包括：
/// - 亮度/对比度/Gamma调整
/// - 伪彩映射
/// - 旋转/翻转
/// - 色阶调整
/// 
/// 使用流程：
/// 1. 调用Camera_SetImageProcessingEnabled启用处理
/// 2. 调用Camera_SetImageProcessingValue设置参数
/// 3. 调用本函数获取处理后图像
/// 
/// 注意：
/// - 处理需要额外的CPU时间
/// - 同样需要调用ReleaseFrame释放
/// </remarks>
REVEALER_API ErrorCode Camera_GetProcessedFrame(CameraHandle handle, ImageData* pImage, unsigned int timeout)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pImage) return -1;

    SC_Frame frame;
    frame.pData = nullptr;

    // 获取处理后的图像
    int ret = SC_GetProcessedFrame(sdkHandle, &frame, timeout);
    if (ret != SC_OK) return ret;

    // 验证数据有效性
    if (frame.frameInfo.width == 0 || frame.frameInfo.height == 0 ||
        frame.frameInfo.size == 0 || !frame.pData)
    {
        SC_ReleaseFrame(sdkHandle, &frame);
        return -1;
    }

    // 填充数据
    pImage->width = frame.frameInfo.width;
    pImage->height = frame.frameInfo.height;
    pImage->pixelFormat = frame.frameInfo.pixelFormat;
    pImage->dataSize = frame.frameInfo.size;
    pImage->stride = frame.frameInfo.size / frame.frameInfo.height;
    pImage->blockId = frame.frameInfo.frameId;
    pImage->timeStamp = frame.frameInfo.timeStamp;
    pImage->pData = (unsigned char*)frame.pData;

    return SC_OK;
}

REVEALER_API ErrorCode Camera_OpenRecord(CameraHandle handle, RecordParam* pParam)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pParam) return -1;

    // 转换为SDK的录像参数结构
    SC_RecordParam recordParam;

    // 注意：SDK的RecordParam使用recordFilePath和fileName分别存储路径和文件名
    // 我们简化为一个fileName字段，需要分离路径和文件名

    // 简化处理：假设fileName包含完整路径
    // 更好的方式是分离路径和文件名，这里先简单处理
    strncpy_s(recordParam.fileName, sizeof(recordParam.fileName),
        pParam->fileName, _TRUNCATE);

    // 如果需要分离路径和文件名，可以这样：
    // 找到最后一个路径分隔符
    // const char* lastSlash = strrchr(pParam->fileName, '\\');
    // if (!lastSlash) lastSlash = strrchr(pParam->fileName, '/');
    // if (lastSlash) {
    //     size_t pathLen = lastSlash - pParam->fileName;
    //     strncpy_s(recordParam.recordFilePath, pathLen + 1, pParam->fileName, pathLen);
    //     strncpy_s(recordParam.fileName, sizeof(recordParam.fileName), lastSlash + 1, _TRUNCATE);
    // }

    // 设置其他参数
    recordParam.recordFormat = static_cast<SC_EVideoType>(pParam->recordFormat);
    recordParam.quality = pParam->quality;
    recordParam.frameRate = static_cast<float>(pParam->frameRate);
    recordParam.startFrame = 0;  // 使用默认值
    recordParam.count = 0;       // 0表示持续录制
    recordParam.saveImageType = eOriginalImage; // 录制原始图像

    return SC_OpenRecord(sdkHandle, &recordParam);
}

/// <summary>
/// 关闭录像
/// </summary>
/// <param name="handle">设备句柄</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// - 停止录像并关闭文件
/// - 文件会被正确finalize
/// - 可以安全播放录制的文件
/// </remarks>
REVEALER_API ErrorCode Camera_CloseRecord(CameraHandle handle)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_CloseRecord(sdkHandle);
}

/// <summary>
/// 设置导出时使用的缓存大小
/// 用于优化大量图像导出的性能
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="cacheSizeInByte">缓存大小（字节）
///   推荐值：100MB - 1GB
///   取决于可用内存和导出速度需求
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
REVEALER_API ErrorCode Camera_SetExportCacheSize(CameraHandle handle, unsigned long long cacheSizeInByte)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;
    return SC_SetExportCacheSize(sdkHandle, cacheSizeInByte);
}

// =================================================================
// 5.6 属性操作
// =================================================================

/// <summary>
/// 检查属性是否可用
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称，如"Width"、"ExposureTime"</param>
/// <returns>1=可用，0=不可用</returns>
/// <remarks>
/// 属性可用性取决于：
/// - 相机型号和固件版本
/// - 当前相机状态（如是否正在采集）
/// - 其他属性的设置（属性间依赖）
/// 
/// 建议：
/// - 设置属性前先检查可用性
/// - 不可用时不要尝试设置
/// </remarks>
REVEALER_API int Camera_FeatureIsAvailable(CameraHandle handle, const char* featureName)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return 0;

    return SC_FeatureIsAvailable(sdkHandle, featureName) ? 1 : 0;
}

/// <summary>
/// 检查属性是否可读
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <returns>1=可读，0=不可读</returns>
REVEALER_API int Camera_FeatureIsReadable(CameraHandle handle, const char* featureName)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return 0;

    return SC_FeatureIsReadable(sdkHandle, featureName) ? 1 : 0;
}

/// <summary>
/// 检查属性是否可写
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <returns>1=可写，0=不可写</returns>
/// <remarks>
/// 某些属性只读，如：
/// - SensorWidth, SensorHeight（传感器物理尺寸）
/// - DeviceTemperature（当前温度）
/// 
/// 某些属性在采集时不可写，如：
/// - Width, Height, PixelFormat（需要停止采集）
/// </remarks>
REVEALER_API int Camera_FeatureIsWriteable(CameraHandle handle, const char* featureName)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return 0;

    return SC_FeatureIsWriteable(sdkHandle, featureName) ? 1 : 0;
}

/// <summary>
/// 获取属性类型
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <param name="pType">输出：属性类型
///   0 = Integer   (整数，如Width)
///   1 = Float     (浮点数，如ExposureTime)
///   2 = Enum      (枚举，如PixelFormat)
///   3 = Bool      (布尔，如FanSwitch)
///   4 = String    (字符串，如序列号)
///   5 = Command   (命令，如TriggerSoftware)
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 用途：
/// - 动态属性访问
/// - 通用属性编辑器
/// - 自动生成UI
/// </remarks>
REVEALER_API ErrorCode Camera_GetFeatureType(CameraHandle handle, const char* featureName, int* pType)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pType) return -1;

    SC_EFeatureType type;
    int ret = SC_GetFeatureType(sdkHandle, featureName, &type);
    if (ret == SC_OK)
    {
        *pType = static_cast<int>(type);
    }
    return ret;
}

// =================================================================
// Integer属性操作
// =================================================================

/// <summary>
/// 获取整型属性值
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <param name="pValue">输出：属性值</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 常见整型属性：
/// - Width, Height: 图像尺寸
/// - OffsetX, OffsetY: 图像偏移
/// - SensorWidth, SensorHeight: 传感器尺寸（只读）
/// - DeviceTemperatureTarget: 目标温度
/// </remarks>
REVEALER_API ErrorCode Camera_GetIntFeatureValue(CameraHandle handle, const char* featureName, long long* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    int64_t value = 0;
    int ret = SC_GetIntFeatureValue(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取整型属性最小值
/// </summary>
REVEALER_API ErrorCode Camera_GetIntFeatureMin(CameraHandle handle, const char* featureName, long long* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    int64_t value = 0;
    int ret = SC_GetIntFeatureMin(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取整型属性最大值
/// </summary>
REVEALER_API ErrorCode Camera_GetIntFeatureMax(CameraHandle handle, const char* featureName, long long* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    int64_t value = 0;
    int ret = SC_GetIntFeatureMax(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取整型属性步进值
/// </summary>
/// <remarks>
/// 步进值说明：
/// - 有效值必须是：Min + N * Inc（N为整数）
/// - 例如：Min=0, Max=1000, Inc=4
///   有效值：0, 4, 8, 12, ..., 1000
/// 
/// 设置时SDK会自动调整到最近的有效值
/// </remarks>
REVEALER_API ErrorCode Camera_GetIntFeatureInc(CameraHandle handle, const char* featureName, long long* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    int64_t value = 0;
    int ret = SC_GetIntFeatureInc(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 设置整型属性值
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <param name="value">要设置的值</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要提示（参见SDK文档3.3节）：
/// 
/// 1. 属性联动：设置某属性可能影响其他属性
///    例如：设置Width会影响AcquisitionFrameRate
/// 
/// 2. 值自动调整：设置值会被调整到合法范围
///    - 会对齐到步进值（Inc）
///    - 会限制在Min-Max范围内
/// 
/// 3. 必须回读确认：设置后务必重新读取实际生效值
///    正确做法：
///    Camera_SetIntFeatureValue(handle, "Width", 1000);
///    Camera_GetIntFeatureValue(handle, "Width", &actualWidth);
///    // actualWidth可能不是1000，可能是1004（对齐Inc）
/// 
/// 4. 受影响属性：参考SDK文档表3.3.1
///    或使用SC_SubscribeParamUpdateArg回调获取
/// </remarks>
REVEALER_API ErrorCode Camera_SetIntFeatureValue(CameraHandle handle, const char* featureName, long long value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return -1;

    return SC_SetIntFeatureValue(sdkHandle, featureName, value);
}

// =================================================================
// Float属性操作
// =================================================================

/// <summary>
/// 获取浮点属性值
/// </summary>
/// <remarks>
/// 常见浮点属性：
/// - ExposureTime: 曝光时间（微秒）
/// - AcquisitionFrameRate: 采集帧率（fps）
/// - TriggerDelay: 触发延迟（微秒）
/// - DeviceTemperature: 当前温度（℃，只读）
/// </remarks>
REVEALER_API ErrorCode Camera_GetFloatFeatureValue(CameraHandle handle, const char* featureName, double* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    double value = 0;
    int ret = SC_GetFloatFeatureValue(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取浮点属性最小值
/// </summary>
REVEALER_API ErrorCode Camera_GetFloatFeatureMin(CameraHandle handle, const char* featureName, double* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    double value = 0;
    int ret = SC_GetFloatFeatureMin(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取浮点属性最大值
/// </summary>
REVEALER_API ErrorCode Camera_GetFloatFeatureMax(CameraHandle handle, const char* featureName, double* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    double value = 0;
    int ret = SC_GetFloatFeatureMax(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 获取浮点属性步进值（如果有）
/// </summary>
/// <remarks>
/// 注意：不是所有浮点属性都有步进值
/// 返回0可能表示无步进限制
/// </remarks>
REVEALER_API ErrorCode Camera_GetFloatFeatureInc(CameraHandle handle, const char* featureName, double* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    double value = 0;
    int ret = SC_GetFloatFeatureInc(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 设置浮点属性值
/// </summary>
/// <remarks>
/// 注意：同样遵循属性联动规则（见SetIntFeatureValue注释）
/// 
/// 特殊说明：
/// - ExposureTime和AcquisitionFrameRate互相影响
/// - 设置后必须回读确认实际值
/// </remarks>
REVEALER_API ErrorCode Camera_SetFloatFeatureValue(CameraHandle handle, const char* featureName, double value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return -1;

    return SC_SetFloatFeatureValue(sdkHandle, featureName, value);
}

// =================================================================
// Enum枚举属性操作
// =================================================================

/// <summary>
/// 获取枚举属性值
/// </summary>
/// <param name="pValue">输出：枚举值（整数）</param>
/// <remarks>
/// 常见枚举属性（参见Gloria 4.2属性表）：
/// 
/// BinningMode（合并模式）：
///   0 = OneByOne (1x1)
///   1 = TwoByTwo (2x2)
///   2 = FourByFour (4x4)
/// 
/// PixelFormat（像素格式）：
///   17825797 = Mono12
///   17563719 = Mono12p
///   17825799 = Mono16
/// 
/// ReadoutMode（读出模式）：
///   0 = bit11_HS_Low (11位高速低增益)
///   1 = bit11_HS_High (11位高速高增益)
///   6 = bit12_CMS (12位低噪声)
///   7 = bit16_From11 (16位高动态)
/// 
/// FrameRateEnable（帧率使能）：
///   0 = FALSE
///   1 = TRUE
/// </remarks>
REVEALER_API ErrorCode Camera_GetEnumFeatureValue(CameraHandle handle, const char* featureName, unsigned long long* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    uint64_t value = 0;
    int ret = SC_GetEnumFeatureValue(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 设置枚举属性值
/// </summary>
/// <param name="value">枚举值（整数）</param>
/// <remarks>
/// 使用示例：
/// // 设置像素格式为Mono16
/// Camera_SetEnumFeatureValue(handle, "PixelFormat", 17825799);
/// 
/// // 设置读出模式为低噪声模式
/// Camera_SetEnumFeatureValue(handle, "ReadoutMode", 6);
/// </remarks>
REVEALER_API ErrorCode Camera_SetEnumFeatureValue(CameraHandle handle, const char* featureName, unsigned long long value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return -1;

    return SC_SetEnumFeatureValue(sdkHandle, featureName, value);
}

/// <summary>
/// 获取枚举属性的可选值数量
/// </summary>
/// <param name="pNum">输出：可选值数量</param>
/// <remarks>
/// 用途：
/// - 动态生成UI下拉列表
/// - 遍历所有可选值
/// </remarks>
REVEALER_API ErrorCode Camera_GetEnumFeatureEntryNum(CameraHandle handle, const char* featureName, unsigned int* pNum)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pNum) return -1;

    return SC_GetEnumFeatureEntryNum(sdkHandle, featureName, pNum);
}

/// <summary>
/// 获取枚举属性的可设枚举值列表
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="featureName">属性名称</param>
/// <param name="pEntryNum">输入输出：输入时为缓冲区大小，输出时为实际获取数量</param>
/// <param name="pEnumValues">输出：枚举值数组，可为nullptr仅查询数量</param>
/// <param name="pSymbols">输出：符号名称数组，可为nullptr</param>
/// <param name="symbolSize">每个符号名称的缓冲区大小</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 典型用法（两步调用）：
/// 
/// // 第一步：获取数量
/// unsigned int count = 0;
/// Camera_GetEnumFeatureEntryNum(handle, "PixelFormat", &count);
/// 
/// // 第二步：获取完整列表
/// uint64_t* values = new uint64_t[count];
/// char** symbols = new char*[count];
/// for(int i=0; i<count; i++) symbols[i] = new char[256];
/// unsigned int actualCount = count;
/// Camera_GetEnumFeatureEntrys(handle, "PixelFormat", &actualCount, values, symbols, 256);
/// 
/// 注意：
/// - 必须先调用Camera_GetEnumFeatureEntryNum获取正确的数量
/// - pEntryNum作为输入时表示缓冲区大小，作为输出表示实际填充数量
/// </remarks>
REVEALER_API ErrorCode Camera_GetEnumFeatureEntrys(CameraHandle handle, const char* featureName,
    unsigned int* pEntryNum, unsigned long long* pEnumValues, char** pSymbols, int symbolSize)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pEntryNum) return -1;

    // 如果仅查询数量，直接调用GetEnumFeatureEntryNum
    if (pEnumValues == nullptr)
    {
        return SC_GetEnumFeatureEntryNum(sdkHandle, featureName, pEntryNum);
    }

    // 准备SDK的枚举列表结构
    SC_EnumEntryList entryList;
    entryList.enumEntryBufferSize = *pEntryNum;

    // 分配临时缓冲区
    entryList.pEnumEntryInfo = new SC_EnumEntryInfo[*pEntryNum];
    memset(entryList.pEnumEntryInfo, 0, sizeof(SC_EnumEntryInfo) * (*pEntryNum));

    // 调用SDK接口
    int ret = SC_GetEnumFeatureEntrys(sdkHandle, featureName, &entryList);

    if (ret == SC_OK)
    {
        // 复制数据（最多复制缓冲区大小的数量）
        for (unsigned int i = 0; i < entryList.enumEntryBufferSize; i++)
        {
            // 复制枚举值
            pEnumValues[i] = entryList.pEnumEntryInfo[i].value;

            // 如果需要符号名称且提供了缓冲区
            if (pSymbols != nullptr && pSymbols[i] != nullptr && symbolSize > 0)
            {
                strncpy_s(pSymbols[i], symbolSize,
                    entryList.pEnumEntryInfo[i].name, _TRUNCATE);
            }
        }

        // 返回实际填充的数量（等于缓冲区大小）
        *pEntryNum = entryList.enumEntryBufferSize;
    }

    // 释放临时缓冲区
    delete[] entryList.pEnumEntryInfo;

    return ret;
}

/// <summary>
/// 获取枚举属性的符号名称（字符串形式）
/// </summary>
/// <param name="symbol">输出：符号名称缓冲区</param>
/// <param name="symbolSize">缓冲区大小</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 返回当前值的符号名称，如：
/// - BinningMode: "TwoByTwo"
/// - PixelFormat: "Mono16"
/// - ReadoutMode: "bit12_CMS"
/// 
/// 符号名称比数字更易读，适合日志和UI显示
/// </remarks>
REVEALER_API ErrorCode Camera_GetEnumFeatureSymbol(CameraHandle handle, const char* featureName, char* symbol, int symbolSize)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !symbol) return -1;

    SC_String str;
    int ret = SC_GetEnumFeatureSymbol(sdkHandle, featureName, &str);
    if (ret == SC_OK)
    {
        strncpy_s(symbol, symbolSize, str.str, _TRUNCATE);
    }
    return ret;
}

/// <summary>
/// 通过符号名称设置枚举属性
/// </summary>
/// <param name="symbol">符号名称，如"Mono16"</param>
/// <remarks>
/// 使用示例：
/// Camera_SetEnumFeatureSymbol(handle, "PixelFormat", "Mono16");
/// Camera_SetEnumFeatureSymbol(handle, "BinningMode", "TwoByTwo");
/// 
/// 比使用数字更清晰，但需要知道正确的符号名称
/// </remarks>
REVEALER_API ErrorCode Camera_SetEnumFeatureSymbol(CameraHandle handle, const char* featureName, const char* symbol)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !symbol) return -1;

    return SC_SetEnumFeatureSymbol(sdkHandle, featureName, symbol);
}

// =================================================================
// Bool布尔属性操作
// =================================================================

/// <summary>
/// 获取布尔属性值
/// </summary>
/// <param name="pValue">输出：1=true, 0=false</param>
/// <remarks>
/// 常见布尔属性：
/// - FanSwitch: 风扇开关
/// </remarks>
REVEALER_API ErrorCode Camera_GetBoolFeatureValue(CameraHandle handle, const char* featureName, int* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    bool value = false;
    int ret = SC_GetBoolFeatureValue(sdkHandle, featureName, &value);
    if (ret == SC_OK) *pValue = value ? 1 : 0;
    return ret;
}

/// <summary>
/// 设置布尔属性值
/// </summary>
/// <param name="value">1=true, 0=false</param>
REVEALER_API ErrorCode Camera_SetBoolFeatureValue(CameraHandle handle, const char* featureName, int value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return -1;

    return SC_SetBoolFeatureValue(sdkHandle, featureName, value != 0);
}

// =================================================================
// String字符串属性操作
// =================================================================

/// <summary>
/// 获取字符串属性值
/// </summary>
/// <param name="pValue">输出：字符串缓冲区</param>
/// <param name="valueSize">缓冲区大小</param>
/// <remarks>
/// 字符串属性通常是设备信息，如：
/// - DeviceSerialNumber: 序列号
/// - DeviceModelName: 型号名称
/// - DeviceVersion: 固件版本
/// </remarks>
REVEALER_API ErrorCode Camera_GetStringFeatureValue(CameraHandle handle, const char* featureName, char* pValue, int valueSize)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    SC_String str;
    int ret = SC_GetStringFeatureValue(sdkHandle, featureName, &str);
    if (ret == SC_OK)
    {
        strncpy_s(pValue, valueSize, str.str, _TRUNCATE);
    }
    return ret;
}

/// <summary>
/// 设置字符串属性值
/// </summary>
/// <remarks>
/// 大部分字符串属性是只读的
/// 可写的字符串属性较少，通常用于配置
/// </remarks>
REVEALER_API ErrorCode Camera_SetStringFeatureValue(CameraHandle handle, const char* featureName, const char* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName || !pValue) return -1;

    return SC_SetStringFeatureValue(sdkHandle, featureName, pValue);
}

// =================================================================
// Command命令属性操作
// =================================================================

/// <summary>
/// 执行命令属性
/// </summary>
/// <param name="featureName">命令名称</param>
/// <remarks>
/// Command类型属性是可执行的命令，无参数无返回值
/// 
/// 常见命令：
/// - TriggerSoftware: 发送软件触发
/// 
/// 使用示例：
/// Camera_ExecuteCommandFeature(handle, "TriggerSoftware");
/// </remarks>
REVEALER_API ErrorCode Camera_ExecuteCommandFeature(CameraHandle handle, const char* featureName)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !featureName) return -1;

    return SC_ExecuteCommandFeature(sdkHandle, featureName);
}

// =================================================================
// 5.7 事件回调操作
// =================================================================

/// <summary>
/// 设备连接状态事件回调注册
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="proc">设备连接状态事件回调函数
///   回调参数说明：
///   - isConnected: 1=设备已连接，0=设备已断开
///   - cameraKey: 设备的序列号（唯一标识）
///   - pUser: 用户自定义数据
/// </param>
/// <param name="pUser">用户自定义数据，可设为NULL</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要说明：
/// - 只需注册一次，对所有USB设备热插拔生效
/// - 回调在独立线程中执行，注意线程安全
/// - 适用场景：监控设备热插拔，自动重连等
/// 
/// 使用示例：
/// void onConnect(int isConnected, const char* cameraKey, void* pUser) {
///     if (isConnected) {
///         printf("Device connected: %s\n", cameraKey);
///     } else {
///         printf("Device disconnected: %s\n", cameraKey);
///     }
/// }
/// Camera_SubscribeConnectArg(handle, onConnect, nullptr);
/// </remarks>
REVEALER_API ErrorCode Camera_SubscribeConnectArg(CameraHandle handle, ConnectCallBack proc, void* pUser)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !proc) return -1;

    // 保存用户回调信息
    CallbackInfo info;
    info.userCallback = reinterpret_cast<void*>(proc);
    info.userData = pUser;
    g_connectCallbackMap[handle] = info;

    // 注册SDK回调，传递handle作为用户数据
    return SC_SubscribeConnectArg(sdkHandle, OnConnectCallback, handle);
}

/// <summary>
/// 参数更新事件回调注册
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="proc">参数更新注册的事件回调函数
///   回调参数说明：
///   - featureName: 受影响的属性名称
///   - pUser: 用户自定义数据
/// </param>
/// <param name="pUser">用户自定义数据，可设为NULL</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要说明：
/// - 只需注册一次，关闭相机后需要重新注册
/// - 当设置某属性导致其他属性联动变化时触发
/// - 参考文档3.3节的属性联动表
/// 
/// 特殊处理属性：
/// - ExposureTime（曝光时间）不会通过此回调通知
/// - AcquisitionFrameRate（采集帧率）不会通过此回调通知
/// - 这两个属性必须主动回读确认实际值
/// 
/// 使用场景：
/// - 动态更新UI显示
/// - 同步多个属性状态
/// - 记录属性变化日志
/// 
/// 使用示例：
/// void onParamUpdate(const char* featureName, void* pUser) {
///     printf("Parameter updated: %s\n", featureName);
///     // 重新读取该属性的值以更新UI
/// }
/// Camera_SubscribeParamUpdateArg(handle, onParamUpdate, nullptr);
/// </remarks>
REVEALER_API ErrorCode Camera_SubscribeParamUpdateArg(CameraHandle handle, ParamUpdateCallBack proc, void* pUser)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !proc) return -1;

    // 保存用户回调信息
    CallbackInfo info;
    info.userCallback = reinterpret_cast<void*>(proc);
    info.userData = pUser;
    g_paramUpdateCallbackMap[handle] = info;

    // 注册SDK回调，传递handle作为用户数据
    return SC_SubscribeParamUpdateArg(sdkHandle, OnParamUpdateCallback, handle);
}

/// <summary>
/// 订阅导出状态通知回调函数
/// </summary>
/// <param name="handle">设备句柄</param>
/// <param name="proc">导出状态回调
///   回调参数说明：
///   - status: 导出状态
///     0 = eExportStart      - 导出开始
///     1 = eExportProcessing - 导出进行中
///     2 = eExportFinish     - 导出完成
///     3 = eExportClose      - 导出关闭
///   - progress: 导出进度（0-100）
///   - pUser: 用户自定义数据
/// </param>
/// <param name="pUser">用户自定义数据，可设为NULL</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 重要说明：
/// - 只需注册一次
/// - 用于监控大量图像导出的进度
/// - 回调在独立线程中执行
/// 
/// 适用场景：
/// - 批量导出图像时显示进度条
/// - 导出完成后的通知处理
/// - 导出失败的错误处理
/// 
/// 使用示例：
/// void onExport(int status, int progress, void* pUser) {
///     switch(status) {
///         case 0: printf("Export started\n"); break;
///         case 1: printf("Exporting... %d%%\n", progress); break;
///         case 2: printf("Export completed!\n"); break;
///         case 3: printf("Export closed\n"); break;
///     }
/// }
/// Camera_SubscribeExportNotify(handle, onExport, nullptr);
/// </remarks>
REVEALER_API ErrorCode Camera_SubscribeExportNotify(CameraHandle handle, ExportEventCallBack proc, void* pUser)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !proc) return -1;

    // 保存用户回调信息
    CallbackInfo info;
    info.userCallback = reinterpret_cast<void*>(proc);
    info.userData = pUser;
    g_exportCallbackMap[handle] = info;

    // 注册SDK回调，传递handle作为用户数据
    // 注意：ExportEventCB的签名与其他回调不同
    return SC_SubscribeExportNotify(sdkHandle, OnExportCallback, handle);
}

// =================================================================
// 5.8 其他功能（高级图像处理）
// =================================================================

/// <summary>
/// 设置自动曝光参数
/// </summary>
/// <param name="mode">自动曝光模式：
///   0 = 中央模式（eAutoExpCenter）- 根据图像中央区域调整
///   1 = 右侧模式（eAutoExpRight）- 根据图像右侧区域调整
///   2 = 关闭（eAutoExpInvalid）
/// </param>
/// <param name="targetGray">目标灰度值
///   范围：通常0-255
///   -1 = 使用默认值
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 自动曝光工作原理：
/// 1. 采集一帧图像
/// 2. 计算指定区域的平均灰度
/// 3. 与目标灰度比较
/// 4. 调整曝光时间使灰度接近目标值
/// 
/// 模式选择：
/// - Center: 适合中央区域为主要关注点
/// - Right: 适合右侧有重要内容的场景
/// - Invalid: 关闭自动曝光
/// 
/// 使用流程：
/// 1. 调用SetAutoExposureParam设置参数
/// 2. 调用AutoExposure执行自动曝光
/// 3. SDK自动调整ExposureTime属性
/// </remarks>
REVEALER_API ErrorCode Camera_SetAutoExposureParam(CameraHandle handle, int mode, int targetGray)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_AutoExpParam param;
    param.origTargetGray = targetGray;

    // 转换模式枚举
    switch (mode)
    {
    case 0: param.mode = eAutoExpCenter; break;
    case 1: param.mode = eAutoExpRight; break;
    case 2: param.mode = eAutoExpInvalid; break;
    default: param.mode = eAutoExpInvalid; break;
    }

    return SC_SetAutoExposureParam(sdkHandle, &param);
}

/// <summary>
/// 执行自动曝光
/// </summary>
/// <param name="pActualGray">输出：实际达到的目标灰度</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 注意：
/// - 必须在采集状态下调用
/// - 会自动调整ExposureTime
/// - 调整后需要重新GetExposureTime获取实际值
/// </remarks>
REVEALER_API ErrorCode Camera_AutoExposure(CameraHandle handle, int* pActualGray)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_AutoExpParam param;
    int ret = SC_AutoExposure(sdkHandle, &param);

    if (ret == SC_OK && pActualGray)
    {
        *pActualGray = param.origTargetGray;
    }

    return ret;
}

/// <summary>
/// 设置自动色阶模式
/// </summary>
/// <param name="mode">色阶模式：
///   0 = 关闭（eAutoLevelOff）
///   1 = 右色阶（eAutoLevelR）- 自动调整高亮部分
///   2 = 左色阶（eAutoLevelL）- 自动调整暗部
///   3 = 左右色阶（eAutoLevelRL）- 同时调整暗部和高亮
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 色阶调整原理：
/// - 左色阶：调整图像最暗值映射
/// - 右色阶：调整图像最亮值映射
/// - 目的：充分利用显示范围，增强对比度
/// 
/// 典型应用：
/// - 暗场图像：使用左色阶提升亮度
/// - 过曝图像：使用右色阶压缩高光
/// - 低对比度：使用左右色阶同时调整
/// 
/// 注意：
/// - 色阶是图像处理操作
/// - 不改变相机采集参数
/// - 仅影响输出图像的显示效果
/// </remarks>
REVEALER_API ErrorCode Camera_SetAutoLevels(CameraHandle handle, int mode)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_AutoLevelMode levelMode;
    switch (mode)
    {
    case 0: levelMode = eAutoLevelOff; break;
    case 1: levelMode = eAutoLevelR; break;
    case 2: levelMode = eAutoLevelL; break;
    case 3: levelMode = eAutoLevelRL; break;
    default: levelMode = eAutoLevelOff; break;
    }

    return SC_SetAutoLevels(sdkHandle, levelMode);
}

/// <summary>
/// 获取自动色阶模式
/// </summary>
REVEALER_API ErrorCode Camera_GetAutoLevels(CameraHandle handle, int* pMode)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pMode) return -1;

    SC_AutoLevelMode mode = eAutoLevelOff;
    int ret = SC_GetAutoLevels(sdkHandle, mode);

    if (ret == SC_OK)
    {
        switch (mode)
        {
        case eAutoLevelOff: *pMode = 0; break;
        case eAutoLevelR: *pMode = 1; break;
        case eAutoLevelL: *pMode = 2; break;
        case eAutoLevelRL: *pMode = 3; break;
        default: *pMode = 0; break;
        }
    }

    return ret;
}

/// <summary>
/// 设置色阶阈值
/// </summary>
/// <param name="mode">1=右色阶, 2=左色阶</param>
/// <param name="value">阈值
///   范围：0-65535
///   左色阶：小于此值的像素映射为0
///   右色阶：大于此值的像素映射为最大值
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 使用示例：
/// // 设置左色阶阈值为100（暗部裁剪）
/// Camera_SetAutoLevelValue(handle, 2, 100);
/// 
/// // 设置右色阶阈值为60000（高光裁剪）
/// Camera_SetAutoLevelValue(handle, 1, 60000);
/// 
/// 自动调整：
/// - 如果不手动设置，ExecuteAutoLevel会自动计算合适的阈值
/// </remarks>
REVEALER_API ErrorCode Camera_SetAutoLevelValue(CameraHandle handle, int mode, int value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    // 限制范围
    if (value < 0) value = 0;
    if (value > 65535) value = 65535;

    SC_AutoLevelMode levelMode = (mode == 1) ? eAutoLevelR : eAutoLevelL;
    return SC_SetAutoLevelValue(sdkHandle, levelMode, value);
}

/// <summary>
/// 获取色阶阈值
/// </summary>
REVEALER_API ErrorCode Camera_GetAutoLevelValue(CameraHandle handle, int mode, int* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pValue) return -1;

    SC_AutoLevelMode levelMode = (mode == 1) ? eAutoLevelR : eAutoLevelL;
    int value = 0;
    int ret = SC_GetAutoLevelValue(sdkHandle, levelMode, value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 执行一次自动色阶
/// </summary>
/// <param name="mode">
///   1 = 仅右色阶
///   2 = 仅左色阶
///   3 = 左右色阶（推荐）
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 执行后：
/// - SDK自动分析当前图像
/// - 计算最佳色阶阈值
/// - 更新色阶参数
/// - 后续图像应用新的色阶映射
/// 
/// 使用时机：
/// - 光照条件改变
/// - 场景切换
/// - 对比度不足时
/// </remarks>
REVEALER_API ErrorCode Camera_ExecuteAutoLevel(CameraHandle handle, int mode)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_AutoLevelMode levelMode;
    switch (mode)
    {
    case 1: levelMode = eAutoLevelR; break;
    case 2: levelMode = eAutoLevelL; break;
    case 3: levelMode = eAutoLevelRL; break;
    default: levelMode = eAutoLevelRL; break;
    }

    return SC_ExecuteAutoLevel(sdkHandle, levelMode);
}

/// <summary>
/// 设置图像处理功能使能
/// </summary>
/// <param name="feature">处理功能类型（参见SDK文档6.1）：
///   0 = eBrightness  - 亮度调整 [-100, 100], 默认50
///   1 = eContrast    - 对比度 [0, 100], 默认50
///   2 = eGamma       - Gamma校正 [0, 100], 默认56
///   3 = ePseudoColor - 伪彩映射（见枚举定义）
///   4 = eRotation    - 旋转（见枚举定义）
///   5 = eFlip        - 翻转（见枚举定义）
/// </param>
/// <param name="enable">1=启用, 0=禁用</param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 图像处理流程：
/// 1. 启用所需的处理功能
/// 2. 设置处理参数
/// 3. 调用GetProcessedFrame获取处理后图像
/// 
/// 性能考虑：
/// - 每个启用的功能都会增加处理时间
/// - 建议只启用必需的功能
/// - 可以动态开关
/// </remarks>
REVEALER_API ErrorCode Camera_SetImageProcessingEnabled(CameraHandle handle, int feature, int enable)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_ImageProcessingFeature feat = static_cast<SC_ImageProcessingFeature>(feature);
    return SC_SetImageProcessingFeatureEnabled(sdkHandle, feat, enable != 0);
}

/// <summary>
/// 获取图像处理功能使能状态
/// </summary>
REVEALER_API ErrorCode Camera_GetImageProcessingEnabled(CameraHandle handle, int feature, int* pEnable)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pEnable) return -1;

    SC_ImageProcessingFeature feat = static_cast<SC_ImageProcessingFeature>(feature);
    bool enable = false;
    int ret = SC_GetImageProcessingFeatureEnabled(sdkHandle, feat, enable);
    if (ret == SC_OK) *pEnable = enable ? 1 : 0;
    return ret;
}

/// <summary>
/// 设置图像处理参数值
/// </summary>
/// <param name="feature">处理功能类型（0-5）</param>
/// <param name="value">参数值
///   亮度：[-100, 100]
///   对比度：[0, 100]
///   Gamma：[0, 100]
///   伪彩：见SC_PseudoColorMap枚举（6.2节）
///   旋转：见旋转模式枚举（6.5节）
///   翻转：见翻转模式枚举（6.6节）
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 参数说明：
/// 
/// 亮度（Brightness）：
/// - 负值：变暗
/// - 0：不变
/// - 正值：变亮
/// 
/// 对比度（Contrast）：
/// - 小于50：降低对比度
/// - 50：不变
/// - 大于50：增强对比度
/// 
/// Gamma：
/// - 小于56：增强暗部细节
/// - 56：线性（默认）
/// - 大于56：增强亮部细节
/// </remarks>
REVEALER_API ErrorCode Camera_SetImageProcessingValue(CameraHandle handle, int feature, int value)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_ImageProcessingFeature feat = static_cast<SC_ImageProcessingFeature>(feature);
    return SC_SetImageProcessingFeatureValue(sdkHandle, feat, value);
}

/// <summary>
/// 获取图像处理参数值
/// </summary>
REVEALER_API ErrorCode Camera_GetImageProcessingValue(CameraHandle handle, int feature, int* pValue)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pValue) return -1;

    SC_ImageProcessingFeature feat = static_cast<SC_ImageProcessingFeature>(feature);
    int value = 0;
    int ret = SC_GetImageProcessingFeatureValue(sdkHandle, feat, value);
    if (ret == SC_OK) *pValue = value;
    return ret;
}

/// <summary>
/// 设置伪彩映射模式
/// </summary>
/// <param name="mapMode">伪彩模式（参见SDK文档6.2）：
///   0 = eHsv    - HSV色彩映射
///   1 = eJet    - Jet色彩映射（类似Matlab）
///   2 = eRed    - 红色渐变
///   3 = eGreen  - 绿色渐变
///   4 = eBlue   - 蓝色渐变
/// </param>
/// <returns>SC_OK(0)表示成功</returns>
/// <remarks>
/// 伪彩用途：
/// - 将灰度图转换为彩色图
/// - 增强视觉对比度
/// - 便于识别温度分布、强度差异等
/// 
/// 模式选择：
/// - Jet：经典科学可视化，冷热色调明显
/// - HSV：色调变化平滑
/// - Red/Green/Blue：单色渐变，适合特定应用
/// 
/// 使用流程：
/// 1. 启用伪彩功能：SetImageProcessingEnabled(3, 1)
/// 2. 设置映射模式：SetPseudoColorMap(1) // Jet
/// 3. 获取处理图像：GetProcessedFrame
/// </remarks>
REVEALER_API ErrorCode Camera_SetPseudoColorMap(CameraHandle handle, int mapMode)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    SC_PseudoColorMap mode = static_cast<SC_PseudoColorMap>(mapMode);
    return SC_SetPseudoColorMap(sdkHandle, mode);
}

/// <summary>
/// 获取伪彩映射模式
/// </summary>
REVEALER_API ErrorCode Camera_GetPseudoColorMap(CameraHandle handle, int* pMapMode)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !pMapMode) return -1;

    SC_PseudoColorMap mode;
    int ret = SC_GetPseudoColorMap(sdkHandle, mode);
    if (ret == SC_OK)
    {
        *pMapMode = static_cast<int>(mode);
    }
    return ret;
}

REVEALER_API ErrorCode Camera_AttachProcessedGrabbing(CameraHandle handle, FrameCallBack proc, void* pUser)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle || !proc) return -1;

    // 保存用户回调信息
    CallbackInfo info;
    info.userCallback = reinterpret_cast<void*>(proc);
    info.userData = pUser;
    g_processedFrameCallbackMap[handle] = info;

    // 注册SDK回调
    return SC_AttachProImgGrabbing(sdkHandle, OnProcessedFrameCallback, handle);
}

/// <summary>
/// 设置ROI（感兴趣区域）
/// </summary>
REVEALER_API ErrorCode Camera_SetROI(CameraHandle handle, long long width, long long height,
    long long offsetX, long long offsetY)
{
    SC_DEV_HANDLE sdkHandle = GetSDKHandle(handle);
    if (!sdkHandle) return -1;

    return SC_SetROI(sdkHandle, width, height, offsetX, offsetY);
}