#pragma once

#ifdef SIMSCOPHARDWAREREVEALER_EXPORTS
#define REVEALER_API __declspec(dllexport)
#else
#define REVEALER_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

	// =================================================================
	// 枚举类型定义（对应文档第6章）
	// =================================================================

	// 6.1 图像处理属性枚举
	typedef enum {
		ImageProcessing_Brightness = 0,   // 亮度 [-100, 100], 默认50
		ImageProcessing_Contrast = 1,     // 对比度 [0, 100], 默认50
		ImageProcessing_Gamma = 2,        // Gamma [0, 100], 默认56
		ImageProcessing_PseudoColor = 3,  // 伪彩（见伪彩模式枚举）
		ImageProcessing_Rotation = 4,     // 旋转（见旋转模式枚举）
		ImageProcessing_Flip = 5          // 翻转（见翻转模式枚举）
	} ImageProcessingFeature;

	// 6.2 伪彩模式枚举
	typedef enum {
		PseudoColor_HSV = 0,    // HSV色彩映射
		PseudoColor_Jet = 1,    // Jet色彩映射（类似Matlab）
		PseudoColor_Red = 2,    // 红色渐变
		PseudoColor_Green = 3,  // 绿色渐变
		PseudoColor_Blue = 4    // 蓝色渐变
	} PseudoColorMapMode;

	// 6.3 自动曝光模式枚举
	typedef enum {
		AutoExp_Center = 0,   // 中央模式
		AutoExp_Right = 1,    // 右侧模式
		AutoExp_Invalid = 2   // 关闭自动曝光
	} AutoExposureMode;

	// 6.4 自动色阶模式枚举
	typedef enum {
		AutoLevel_Off = 0,   // 关闭
		AutoLevel_R = 1,     // 右色阶
		AutoLevel_L = 2,     // 左色阶
		AutoLevel_RL = 3     // 左右色阶
	} AutoLevelMode;

	// 6.5 旋转模式枚举
	typedef enum {
		Rotate_0 = 0,     // 旋转0度
		Rotate_90 = 1,    // 旋转90度
		Rotate_180 = 2,   // 旋转180度
		Rotate_270 = 3    // 旋转270度
	} RotationMode;

	// 6.6 翻转模式枚举
	typedef enum {
		Flip_X = 0,    // 垂直翻转
		Flip_Y = 1,    // 水平翻转
		Flip_XY = 2    // 垂直+水平翻转
	} FlipMode;

	// 录像格式枚举
	typedef enum {
		RecordFormat_TIFF = 0,        // TIFF格式
		RecordFormat_BMP = 1,         // BMP格式（暂不支持）
		RecordFormat_SCD = 2,         // SCD格式
		RecordFormat_TIFFVideo = 3,   // TIFF格式，单个文件
		RecordFormat_NotSupport = 255 // 不支持
	} RecordFormat;

	// 录像参数结构
	typedef struct {
		char recordFilePath[512];  // 保存路径
		char fileName[512];        // 文件名
		int recordFormat;          // 0=TIFF, 1=BMP(暂不支持), 2=SCD, 3=TIFFVideo
		int quality;               // 0-100
		int frameRate;             // 帧率
		unsigned int startFrame;   // 起始帧（默认0）
		unsigned int count;        // 采集帧数（0=持续录制）
	} RecordParam;

	// 属性类型枚举（对应SC_GetFeatureType返回值）
	typedef enum {
		FeatureType_Integer = 0,   // 整数类型
		FeatureType_Float = 1,     // 浮点类型
		FeatureType_Enum = 2,      // 枚举类型
		FeatureType_Bool = 3,      // 布尔类型
		FeatureType_String = 4,    // 字符串类型
		FeatureType_Command = 5    // 命令类型
	} FeatureType;

	// =================================================================
	// 类型定义
	// =================================================================

	typedef void* CameraHandle;
	typedef int ErrorCode;

	// 图像数据结构
	typedef struct {
		int width;
		int height;
		int stride;
		int pixelFormat;
		unsigned char* pData;
		int dataSize;
		unsigned long long blockId;     // 帧ID
		unsigned long long timeStamp;   // 时间戳
	} ImageData;

	// 设备信息结构
	typedef struct {
		char cameraName[256];
		char serialNumber[256];
		char modelName[256];
		char manufacturerInfo[256];
		char deviceVersion[256];
	} DeviceInfo;

	//// 访问权限枚举
	//typedef enum {
	//    AccessPermissionUnknown = 0,      // 未知
	//    AccessPermissionNone = 1,         // 无访问权限
	//    AccessPermissionMonitor = 2,      // 只读模式
	//    AccessPermissionControl = 3,      // 控制模式
	//    AccessPermissionExclusive = 4     // 独占模式（推荐）
	//} CameraAccessPermission;

	// =================================================================
		// 回调函数类型定义
		// =================================================================

	/// <summary>设备连接状态回调函数类型</summary>
		/// <param name="isConnected">1=已连接, 0=已断开</param>
		/// <param name="cameraKey">设备序列号</param>
		/// <param name="pUser">用户自定义数据</param>
	typedef void (*ConnectCallBack)(int isConnected, const char* cameraKey, void* pUser);

	/// <summary>参数更新回调函数类型</summary>
	/// <param name="featureName">受影响的属性名称</param>
	/// <param name="pUser">用户自定义数据</param>
	typedef void (*ParamUpdateCallBack)(const char* featureName, void* pUser);

	/// <summary>导出状态回调函数类型</summary>
	/// <param name="status">导出状态：0=开始, 1=进行中, 2=完成, 3=关闭</param>
	/// <param name="progress">导出进度(0-100)</param>
	/// <param name="pUser">用户自定义数据</param>
	typedef void (*ExportEventCallBack)(int status, int progress, void* pUser);

	/// <summary>帧数据回调函数类型</summary>
	/// <param name="pImage">图像数据</param>
	/// <param name="pUser">用户自定义数据</param>
	typedef void (*FrameCallBack)(ImageData* pImage, void* pUser);

	// =================================================================
	// 5.1 系统操作
	// =================================================================

	/// <summary>获取SDK版本</summary>
	REVEALER_API const char* Camera_GetVersion();

	/// <summary>初始化SDK</summary>
	/// <param name="logLevel">0=Off, 1=Error, 2=Warn, 3=Info, 4=Debug</param>
	/// <param name="logPath">日志路径</param>
	/// <param name="fileSize">单个日志文件大小(字节)</param>
	/// <param name="fileNum">日志文件数量</param>
	REVEALER_API ErrorCode Camera_Initialize(int logLevel, const char* logPath,
		unsigned int fileSize, unsigned int fileNum);

	/// <summary>释放SDK资源</summary>
	REVEALER_API void Camera_Release();

	/// <summary>枚举设备</summary>
	/// <param name="pDeviceCount">输出：设备数量</param>
	/// <param name="interfaceType">接口类型：0=All, 1=USB3, 2=CXP, 3=Custom</param>
	REVEALER_API ErrorCode Camera_EnumDevices(int* pDeviceCount, unsigned int interfaceType);

	/// <summary>获取设备名称</summary>
	REVEALER_API ErrorCode Camera_GetDeviceName(int index, char* name, int nameSize);

	/// <summary>创建设备句柄</summary>
	/// <param name="pHandle">输出：设备句柄</param>
	/// <param name="deviceIndex">设备索引</param>
	REVEALER_API ErrorCode Camera_CreateHandle(CameraHandle* pHandle, int deviceIndex);

	/// <summary>销毁设备句柄</summary>
	REVEALER_API ErrorCode Camera_DestroyHandle(CameraHandle handle);

	// =================================================================
	// 5.2 相机操作
	// =================================================================

	/// <summary>打开相机（默认权限）</summary>
	REVEALER_API ErrorCode Camera_Open(CameraHandle handle);

	///// <summary>打开相机（指定权限）</summary>
	///// <param name="accessPermission">访问权限：2=Monitor, 3=Control, 4=Exclusive</param>
	//REVEALER_API ErrorCode Camera_OpenEx(CameraHandle handle, int accessPermission);

	/// <summary>关闭相机</summary>
	REVEALER_API ErrorCode Camera_Close(CameraHandle handle);

	// =================================================================
	// 5.3 相机配置下载
	// =================================================================

	/// <summary>下载相机XML配置文件</summary>
	/// <param name="pFullPath">保存路径（包含文件名）</param>
	REVEALER_API ErrorCode Camera_DownloadGenICamXML(CameraHandle handle, const char* pFullPath);

	// =================================================================
	// 5.4 获取设备信息
	// =================================================================

	/// <summary>获取设备详细信息</summary>
	REVEALER_API ErrorCode Camera_GetDeviceInfo(CameraHandle handle, DeviceInfo* pDevInfo);

	// =================================================================
	// 5.5 相机数据流操作
	// =================================================================

	/// <summary>开始采集</summary>
	REVEALER_API ErrorCode Camera_StartGrabbing(CameraHandle handle);

	/// <summary>停止采集</summary>
	REVEALER_API ErrorCode Camera_StopGrabbing(CameraHandle handle);

	/// <summary>检查是否正在采集</summary>
	REVEALER_API ErrorCode Camera_IsGrabbing(CameraHandle handle, int* pIsGrabbing);

	/// <summary>设置帧缓冲区数量</summary>
	REVEALER_API ErrorCode Camera_SetBufferCount(CameraHandle handle, unsigned int bufferCount);

	/// <summary>获取一帧图像（同步）</summary>
	/// <param name="timeout">超时时间(ms)，0xFFFFFFFF表示无限等待</param>
	REVEALER_API ErrorCode Camera_GetFrame(CameraHandle handle, ImageData* pImage, unsigned int timeout);

	/// <summary>释放帧资源</summary>
	REVEALER_API ErrorCode Camera_ReleaseFrame(CameraHandle handle, ImageData* pImage);

	/// <summary>获取处理后的图像（同步）</summary>
	REVEALER_API ErrorCode Camera_GetProcessedFrame(CameraHandle handle, ImageData* pImage, unsigned int timeout);

	/// <summary>打开录像</summary>
	REVEALER_API ErrorCode Camera_OpenRecord(CameraHandle handle, RecordParam* pParam);

	/// <summary>关闭录像</summary>
	REVEALER_API ErrorCode Camera_CloseRecord(CameraHandle handle);

	/// <summary>设置导出缓存大小</summary>
	REVEALER_API ErrorCode Camera_SetExportCacheSize(CameraHandle handle, unsigned long long cacheSizeInByte);

	// =================================================================
	// 5.6 属性操作
	// =================================================================

	// 属性可用性检查
	REVEALER_API int Camera_FeatureIsAvailable(CameraHandle handle, const char* featureName);
	REVEALER_API int Camera_FeatureIsReadable(CameraHandle handle, const char* featureName);
	REVEALER_API int Camera_FeatureIsWriteable(CameraHandle handle, const char* featureName);

	/// <summary>获取属性类型</summary>
	/// <returns>0=Int, 1=Float, 2=Enum, 3=Bool, 4=String, 5=Command</returns>
	REVEALER_API ErrorCode Camera_GetFeatureType(CameraHandle handle, const char* featureName, int* pType);

	// Integer 属性操作
	REVEALER_API ErrorCode Camera_GetIntFeatureValue(CameraHandle handle, const char* featureName, long long* pValue);
	REVEALER_API ErrorCode Camera_GetIntFeatureMin(CameraHandle handle, const char* featureName, long long* pValue);
	REVEALER_API ErrorCode Camera_GetIntFeatureMax(CameraHandle handle, const char* featureName, long long* pValue);
	REVEALER_API ErrorCode Camera_GetIntFeatureInc(CameraHandle handle, const char* featureName, long long* pValue);
	REVEALER_API ErrorCode Camera_SetIntFeatureValue(CameraHandle handle, const char* featureName, long long value);

	// Float 属性操作
	REVEALER_API ErrorCode Camera_GetFloatFeatureValue(CameraHandle handle, const char* featureName, double* pValue);
	REVEALER_API ErrorCode Camera_GetFloatFeatureMin(CameraHandle handle, const char* featureName, double* pValue);
	REVEALER_API ErrorCode Camera_GetFloatFeatureMax(CameraHandle handle, const char* featureName, double* pValue);
	REVEALER_API ErrorCode Camera_GetFloatFeatureInc(CameraHandle handle, const char* featureName, double* pValue);
	REVEALER_API ErrorCode Camera_SetFloatFeatureValue(CameraHandle handle, const char* featureName, double value);

	// Enum 属性操作
	REVEALER_API ErrorCode Camera_GetEnumFeatureValue(CameraHandle handle, const char* featureName, unsigned long long* pValue);
	REVEALER_API ErrorCode Camera_SetEnumFeatureValue(CameraHandle handle, const char* featureName, unsigned long long value);
	REVEALER_API ErrorCode Camera_GetEnumFeatureEntryNum(CameraHandle handle, const char* featureName, unsigned int* pNum);
	REVEALER_API ErrorCode Camera_GetEnumFeatureEntrys(CameraHandle handle, const char* featureName,
		unsigned int* pEntryNum, unsigned long long* pEnumValues, char** pSymbols, int symbolSize);
	REVEALER_API ErrorCode Camera_GetEnumFeatureSymbol(CameraHandle handle, const char* featureName, char* symbol, int symbolSize);
	REVEALER_API ErrorCode Camera_SetEnumFeatureSymbol(CameraHandle handle, const char* featureName, const char* symbol);

	// Bool 属性操作
	REVEALER_API ErrorCode Camera_GetBoolFeatureValue(CameraHandle handle, const char* featureName, int* pValue);
	REVEALER_API ErrorCode Camera_SetBoolFeatureValue(CameraHandle handle, const char* featureName, int value);

	// String 属性操作
	REVEALER_API ErrorCode Camera_GetStringFeatureValue(CameraHandle handle, const char* featureName, char* pValue, int valueSize);
	REVEALER_API ErrorCode Camera_SetStringFeatureValue(CameraHandle handle, const char* featureName, const char* pValue);

	// Command 属性操作
	/// <summary>执行命令属性</summary>
	/// <param name="featureName">命令名称，如"TriggerSoftware"</param>
	/// <remarks>
	/// Command类型属性是可执行的命令，无参数无返回值
	/// 常见命令：
	/// - TriggerSoftware: 发送软件触发
	/// - AcquisitionStart: 开始采集（通常用StartGrabbing代替）
	/// - AcquisitionStop: 停止采集（通常用StopGrabbing代替）
	/// </remarks>
	REVEALER_API ErrorCode Camera_ExecuteCommandFeature(CameraHandle handle, const char* featureName);

	// =================================================================
	// 5.7 事件回调操作
	// =================================================================

	/// <summary>设备连接状态事件回调注册</summary>
	/// <param name="proc">设备连接状态事件回调函数</param>
	/// <param name="pUser">用户自定义数据</param>
	/// <remarks>
	/// 重要说明：
	/// - 只需注册一次，对所有USB设备热插拔生效
	/// - 回调在独立线程中执行，注意线程安全
	/// - 适用场景：监控设备热插拔，自动重连等
	/// </remarks>
	REVEALER_API ErrorCode Camera_SubscribeConnectArg(CameraHandle handle, ConnectCallBack proc, void* pUser);

	/// <summary>参数更新事件回调注册</summary>
	/// <param name="proc">参数更新注册的事件回调函数</param>
	/// <param name="pUser">用户自定义数据</param>
	/// <remarks>
	/// 重要说明：
	/// - 只需注册一次，关闭相机后需要重新注册
	/// - 当设置某属性导致其他属性联动变化时触发
	/// - ExposureTime和AcquisitionFrameRate不会通过此回调通知
	/// </remarks>
	REVEALER_API ErrorCode Camera_SubscribeParamUpdateArg(CameraHandle handle, ParamUpdateCallBack proc, void* pUser);

	/// <summary>订阅导出状态通知回调函数</summary>
	/// <param name="proc">导出状态回调</param>
	/// <param name="pUser">用户自定义数据</param>
	/// <remarks>
	/// 重要说明：
	/// - 只需注册一次
	/// - 用于监控大量图像导出的进度
	/// - 回调在独立线程中执行
	/// </remarks>
	REVEALER_API ErrorCode Camera_SubscribeExportNotify(CameraHandle handle, ExportEventCallBack proc, void* pUser);

	// =================================================================
	// 5.8 其他功能
	// =================================================================

	/// <summary>设置自动曝光参数</summary>
/// <param name="mode">0=中央, 1=右侧, 2=关闭</param>
/// <param name="targetGray">目标灰度值(-1表示使用默认值)</param>
	REVEALER_API ErrorCode Camera_SetAutoExposureParam(CameraHandle handle, int mode, int targetGray);

	/// <summary>执行自动曝光</summary>
	/// <param name="pActualGray">输出：实际目标灰度</param>
	REVEALER_API ErrorCode Camera_AutoExposure(CameraHandle handle, int* pActualGray);

	/// <summary>设置自动色阶模式</summary>
	/// <param name="mode">0=关闭, 1=右, 2=左, 3=左右</param>
	REVEALER_API ErrorCode Camera_SetAutoLevels(CameraHandle handle, int mode);

	/// <summary>获取自动色阶模式</summary>
	REVEALER_API ErrorCode Camera_GetAutoLevels(CameraHandle handle, int* pMode);

	/// <summary>设置自动色阶阈值</summary>
	/// <param name="mode">1=右, 2=左</param>
	REVEALER_API ErrorCode Camera_SetAutoLevelValue(CameraHandle handle, int mode, int value);

	/// <summary>获取自动色阶阈值</summary>
	REVEALER_API ErrorCode Camera_GetAutoLevelValue(CameraHandle handle, int mode, int* pValue);

	/// <summary>执行一次自动色阶</summary>
	REVEALER_API ErrorCode Camera_ExecuteAutoLevel(CameraHandle handle, int mode);

	/// <summary>设置图像处理功能使能</summary>
	/// <param name="feature">0=亮度, 1=对比度, 2=Gamma, 3=伪彩, 4=旋转, 5=翻转</param>
	REVEALER_API ErrorCode Camera_SetImageProcessingEnabled(CameraHandle handle, int feature, int enable);

	/// <summary>获取图像处理功能使能</summary>
	REVEALER_API ErrorCode Camera_GetImageProcessingEnabled(CameraHandle handle, int feature, int* pEnable);

	/// <summary>设置图像处理参数值</summary>
	REVEALER_API ErrorCode Camera_SetImageProcessingValue(CameraHandle handle, int feature, int value);

	/// <summary>获取图像处理参数值</summary>
	REVEALER_API ErrorCode Camera_GetImageProcessingValue(CameraHandle handle, int feature, int* pValue);

	/// <summary>设置伪彩映射模式</summary>
	/// <param name="mapMode">0=HSV, 1=Jet, 2=Red, 3=Green, 4=Blue</param>
	REVEALER_API ErrorCode Camera_SetPseudoColorMap(CameraHandle handle, int mapMode);

	/// <summary>注册处理后图像数据回调函数（异步）</summary>
	/// <param name="proc">回调函数</param>
	/// <param name="pUser">用户自定义数据</param>
	/// <remarks>
	/// - 与Camera_GetProcessedFrame互斥，只能选其一
	/// - 在回调中不要执行耗时操作
	/// </remarks>
	REVEALER_API ErrorCode Camera_AttachProcessedGrabbing(CameraHandle handle, FrameCallBack proc, void* pUser);

	/// <summary>
/// 取消处理后图像的回调注册
/// </summary>
	REVEALER_API ErrorCode Camera_DetachGrabbing(CameraHandle handle);

	/// <summary>获取伪彩映射模式</summary>
	REVEALER_API ErrorCode Camera_GetPseudoColorMap(CameraHandle handle, int* pMapMode);

	/// <summary>设置ROI（感兴趣区域）</summary>
	/// <param name="width">宽度</param>
	/// <param name="height">高度</param>
	/// <param name="offsetX">X偏移</param>
	/// <param name="offsetY">Y偏移</param>
	/// <remarks>一次性设置ROI的所有参数，比单独设置更方便</remarks>
	REVEALER_API ErrorCode Camera_SetROI(CameraHandle handle, long long width, long long height,
		long long offsetX, long long offsetY);


#ifdef __cplusplus
}
#endif