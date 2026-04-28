namespace DHHandle
{
    /// <summary>
    /// 获取数据方式
    /// </summary>
    public enum GetDataTypeEnum
    {
        /// <summary>
        /// 单台仪器获取数据
        /// </summary>
        SingleMachine,
        /// <summary>
        /// 多台仪器获取数据
        /// </summary>
        MultiMachine,
        /// <summary>
        /// 分组 仅5972N和5974N
        /// </summary>
        TeamMachine
    }

    public enum CHANNEL_STYLE
    {
        /// <summary>
        /// 模拟通道
        /// </summary>
        ANALOG_CHANNEL_STYLE,
        /// <summary>
        /// 控制卡转速通道
        /// </summary>
        EXTRA_TACHO_CHANNEL_STYLE,
        /// <summary>
        /// 控制卡信号源通道
        /// </summary>
        EXTRA_SIGNAL_CHANNEL_STYLE,
        /// <summary>
        /// CAN通道
        /// </summary>
        CAN_CHANNEL_STYLE,
        /// <summary>
        /// GPS通道
        /// </summary>
        GPS_CHANNEL_STYLE
    }

    public enum MEASURE_TYPE : int
    {
        /// <summary>
        /// 内输入数采
        /// </summary>
        MEASURE_TYPE_INTERNAL_DA,					// 内输入数采		// CChanGeneralParam
        /// <summary>
        /// 外输入数采
        /// </summary>
        MEASURE_TYPE_EXTERNAL_DA,					// 外输入数采		// CChanGeneralParam
        /// <summary>
        /// 应变应力
        /// </summary>
        MEASURE_TYPE_STRAINMETER,					// 应变应力			// CStrainChanParam 补偿通道？？
        /// <summary>
        /// 电荷传感器
        /// </summary>
        MEASURE_TYPE_SENSOR_PE,						// 压电传感器		// CChanGeneralParam
        /// <summary>
        /// 桥式传感器
        /// </summary>
        MEASURE_TYPE_SENSOR_BT,						// 桥式传感器		// CChanGeneralParam
        /// <summary>
        /// 铂电阻测温
        /// </summary>
        MEASURE_TYPE_TEMPERATURE_PT,				// 铂电阻测温		// CPtTemperatureChnParam
        /// <summary>
        /// 热电偶测温
        /// </summary>
        MEASURE_TYPE_TEMPERATURE_THERMO,			// 热电偶测温		// CThermoTemperatureChnParam
        /// <summary>
        /// 转速测量
        /// </summary>
        MEASURE_TYPE_ROTATE_SPEED,					// 转速测量			// CTachoChnParam
        MEASURE_PULSE_COUNTER = 17,                        // 脉冲计数器测量
        /// <summary>
        /// GPS测量
        /// </summary>
        MEASURE_TYPE_GPS = 30,
        /// <summary>
        /// CAN测量
        /// </summary>
        MEASURE_TYPE_CAN = 31,
        /// <summary>
        /// IO通道
        /// </summary>
        MEASURE_TYPE_IO = 32,
        MEASURE_TYPE_DHCOUT = 36,
        MEASURE_TYPE_TEMP_597XW = 194,
        MEASURE_TYPE_RS422 = 114,
        MEASURE_TYPE_DOUBLE_RS422 = 208,
        MEASURE_TYPE_PWM = 125,
        MEASURE_TYPE_RS485_COMMON_CONTROL = 126,
        MEASURE_TYPE_TEMP_JUFU_CONTROL = 127,
        MEASURE_TYPE_TEMP_SANMU_CONTROL = 128,
        MEASURE_TYPE_POWER_ADG_1000_51_CONTROL = 129,
        MEASURE_TYPE_POWER_CHROMA_62000H_CONTROL = 130,
        MEASURE_TYPE_1540HPROS_COOL_WATER_CONTROL = 131,
        MEASURE_TYPE_SALT_FOG_CONTROL = 132,
        MEASURE_TYPE_TEMP_SHANGJIAODA_CONTROL = 133,
        MEASURE_TYPE_POWER_PBZ40_10_CONTROL = 134,
        MEASURE_TYPE_15PROS_COOL_WATER_CONTROL = 135,
        MEASURE_TYPE_TEMP_U8555P_CONTROL = 180,
        MEASURE_TYPE_DOUBLE_RS422_INERTIAL = 215,
        MEASURE_TYPE_DOUBLE_RS422_WIND = 216,
        MEASURE_TYPE_5859A = 214,
        MEASURE_TYPE_RS422_FBG = 230,
        MEASURE_TYPE_RS422_EX = 238,
        MEASURE_TYPE_VIB_WIRE = 199,
        MEASURE_TYPE_VIB_WIRE_PRESS = 213,
    }

    public enum GPSEnum
    {
        // GPS经度
        GPS_TYPE_LONG = 0,
        // GPS纬度
        GPS_TYPE_LAT = 1,
        // GPS可见卫星数
        BrsGPS_TYPE_VISCOUNT = 2,
        // 追踪卫星数
        BrsGPS_TYPE_TRACKCOUNT = 3,
        // GPS速度
        BrsGPS_TYPE_SPEED = 4,
        // GPS高度
        BrsGPS_TYPE_HEIGHT = 5,
    }

    /// <summary>
    /// 参数定义
    /// </summary>
    public class ParamShowDefine
    {
        /// <summary>
        /// 通道使用标志
        /// </summary>
        public const int SHOW_CHANNEL_USE = 3;
        /// <summary>
        /// 通道测量类型
        /// </summary>
        public const int SHOW_CHANNEL_MEASURETYPE = 4;
        /// <summary>
        /// 满度量程
        /// </summary>
        public const int SHOW_CHANNEL_FULLVALUE = 5;
        /// <summary>
        /// 传感器灵敏度
        /// </summary>
        public const int SHOW_CHANNEL_SENSECOEF = 6;
        /// <summary>
        /// 上限频率
        /// </summary>
        public const int SHOW_CHANNEL_UPFREQ = 10;
        /// <summary>
        /// 输入方式
        /// </summary>
        public const int SHOW_CHANNEL_INPUTMODE = 12;
        // 应变应力
        public const int SHOW_STRAIN_SHOWTYPE = 27; 	/// 应变应力显示类型
        public const int SHOW_STRAIN_BRIDGETYPE = 28; 	/// 桥路方式
        public const int SHOW_STRAIN_GAUGE = 29; 	/// 应变计阻值
        public const int SHOW_STRAIN_LEAD = 30; 	/// 导线阻值
        public const int SHOW_STRAIN_SENSECOEF = 31; 	/// 灵敏度系数
        public const int SHOW_STRAIN_POSION = 32; 	/// 泊松比
        public const int SHOW_STRAIN_ELASTICITY = 33; 	/// 弹性模量
        // 桥式传感器
        public const int SHOW_CHANNEL_BRIDGE_MODE = 34; 	/// 供桥
        public const int SHOW_STRAIN_BRIDGEVOLTAGE = 35; 	/// 桥压
        // 铂电阻测温
        public const int SHOW_PT_TYPE = 38; 	/// 铂电阻类型			
        // 热电偶测温
        public const int SHOW_THERMO_TYPE = 40; 	/// 热电偶类型
        public const int SHOW_THERMO_COOLTEMPERATURE = 41; 	/// 冷端温度
        // 计数器传感器
        //public const int SHOW_PULSE_EXCHANGE = 61; // AB项交换标志
        //public const int SHOW_PULSE_SIGNAL_MODE = 62;	/// 信号模式
        //public const int SHOW_PULSE_X_MODE = 63;	 /// 倍频方式
        //public const int SHOW_PULSE_RESET_MODE = 64;	 /// 复位方式
        //public const int SHOW_PULSE_PHA_REVERSE = 65;	 /// PHA反向
        //public const int SHOW_PULSE_PHB_REVERSE = 66;	 /// PHB反向
        //public const int SHOW_PULSE_INDEX_REVERSE = 67;	 /// Index反向
        //public const int SHOW_PULSE_MAX_POS = 70;	 /// 位置计数器最大限制值
        public const int SHOW_PULSE_RESET_SET = 72;	/// 复位
        //public const int SHOW_PULSE_POSCOEF = 74;	 /// 位置系数

        //电压测量范围
        public const int SHOW_CHANNEL_VOLT_FULLVALUE = 90;
        /// <summary>
        /// 测点名称
        /// </summary>
        public const int SHOW_CHANNEL_NAME = 107;
        /// <summary>
        /// 对应抽点比例的采样频率
        /// </summary>
        public const int SHOW_CHANNEL_SAMPLE = 110;

        /// <summary>
        /// 转速计数器showParamID 
        /// </summary>
        public const int SHOW_COUNTER_SENSOR_TYPE_EX = 267;

        /// <summary>
        /// Can类型 0 是常规can，1是canFD
        /// </summary>
        public const int SHOW_CAN_TYPE = 590;
        /// <summary>
        /// 波特率
        /// </summary>
        public const int SHOW_CAN_BAUDRATE = 83;
        /// <summary>
        /// 波特率 CANFD特殊区分
        /// </summary>
        public const int SHOW_CAN_BAUDRATE2 = 589;
        /// <summary>
        /// CANFD数据长度
        /// </summary>
        public const int SHOW_CAN_DATA_LEN = 591;
        /// <summary>
        /// CANFD BRS 速度适应
        /// </summary>
        public const int SHOW_CAN_BRS = 592;
    }
}
