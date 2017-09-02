namespace Vidarls.Lego
open System

type  ArgumentSize =
| Byte = 0x81 // 1 byte
| Short = 0x82 // 2 bytes
| Int = 0x83 // 4 bytes
| String = 0x84

type ReplyType =
| DirectReply = 0x02
| SystemReply = 0x03
| DirectReplyError = 0x04
| SystenReplyError = 0x05

type Opcode =
| UIReadGetFirmware = 0x810A
| UIWriteLED = 0x821B
| UIButtonPressed = 0x8309
| UIDrawUpdate = 0x8400
| UIDrawClean = 0x8401
| UIDrawPixel = 0x8402
| UIDrawLine = 0x8403
| UIDrawCircle = 0x8404
| UIDrawText = 0x8405
| UIDrawFillRect = 0x8409
| UIDrawRect = 0x840a
| UIDrawInverseRect = 0x8410
| UIDrawSelectFont = 0x8411
| UIDrawTopline = 0x8412
| UIDrawFillWindow = 0x8413
| UIDrawDotLine = 0x8415
| UIDrawFillCircle = 0x8418
| UIDrawBmpFile = 0x841c

| SoundBreak = 0x9400
| SoundTone = 0x9401
| SoundPlay = 0x9402
| SoundRepeat = 0x9403
| SoundService = 0x9404

| InputDeviceGetTypeMode = 0x9905
| InputDeviceGetDeviceName = 0x9915
| InputDeviceGetModeName = 0x9916
| InputDeviceReadyPct = 0x991b
| InputDeviceReadyRaw = 0x991c
| InputDeviceReadySI = 0x991d
| InputDeviceClearAll = 0x990a
| InputDeviceClearChanges = 0x991a

| InputRead = 0x9a
| InputReadExt = 0x9e
| InputReadSI = 0x9d

| OutputStop = 0xa3
| OutputPower = 0xa4
| OutputSpeed = 0xa5
| OutputStart = 0xa6
| OutputPolarity = 0xa7
| OutputReady = 0xaa
| OutputStepPower = 0xac
| OutputTimePower = 0xad
| OutputStepSpeed = 0xae
| OutputTimeSpeed = 0xaf
| OutputStepSync = 0xb0
| OutputTimeSync = 0xb1

| Tst = 0xff

type SystemOpcode =
| BeginDownload = 0x92
| ContinueDownload = 0x93
| CloseFileHandle = 0x98
| CreateDirectory = 0x9b
| DeleteFile = 0x9c

type SystemReplyStatus =
| Success = 0x00
| UnknownHandle = 0x01
| HandleNotReady = 0x02
| CorruptFile = 0x03
| NoHandlesAvailable = 0x04
| NoPermission = 0x05
| IllegalPath = 0x06
| FileExists = 0x07
| EndOfFile = 0x08
| SizeError = 0x09
| UnknownError = 0x0A
| IllegalFilename = 0x0B
| IllegalConnection = 0x0C

/// Type of command to send to the brick
type CommandType =
/// Direct caommand with a reply expected
| DirectReply = 0x00
/// Direct command with no reply
| DirectNoReply = 0x80
/// System command with a reply expected
| SystemReply = 0x01
/// System command with no reply
| SystemNoReply = 0x81

/// Format of sensor data
type Format =
| Percent = 0x10
| Raw = 0x11
| SI = 0x12

/// Polarity / Direction to turn the motor
type Polarity =
| Backward = -0x01
| Opposite = 0x00
| Forward = 0x01

type InputPort =
| One = 0x00 
| Two = 0x01
| Three = 0x02
| Four = 0x03
| A = 0x10
| B = 0x11
| C = 0x12
| D = 0x13

[<Flags>]
type OutputPort =
| A = 0x01
| B = 0x02
| C = 0x04
| D = 0x08
| ALL = 0x0f

/// Devices that can be recognised
/// as input or outpur devices
type DeviceType = 
| NxtTouch = 0x01
| NxtLight = 0x02
| NxtSound = 0x03
| NxtColour = 0x04
| NxtUltrasonic = 0x05
| NxtTempterature = 0x06
| LargeMotor = 0x07
| MediumMotor = 0x08
| Ev3Touch = 0x10
| Ev3Colour = 0x1D
| Ev3Ultrasonic = 0x1E
| Ev3Gyroscope = 0x20
| Ev3Infrared = 0x21
| SensorIsInitializing = 0x7D
| NoDeviceConnected = 0x7E
| DeviceConnectedToWrongPort = 0x7F
| UnknownDevice = 0xff

/// Buttons on the face of the EV3 brick
type BrickButton =
| None = 0x00
| Up = 0x01
| Enter = 0x02
| Down = 0x03
| Right = 0x04
| Left = 0x05
| Back = 0x06
| Any = 0x07

/// Pattern to light up the LED
/// of the EV3 brick
type LedPattern = 
| Off = 0x00
| Green = 0x01
| Red = 0x02
| Orange = 0x03
| GreenFlash = 0x04
| RedFlash = 0x05
| OrangeFlash = 0x06
| GreenPulse = 0x07
| RedPulse = 0x08
| OrangePulse = 0x09

/// UI colours
type Colour =
| Background = 0x00
| Foreground = 0x01

type FontSize = 
| Small = 0x00
| Medium = 0x01
| Large = 0x02

type TouchMode = 
| Touch = 0x00
| Bumps = 0x01

type NxtLightMode =
| Reflect = 0x00
| Ambient = 0x01

type NxtSoundMode = 
| Decibels = 0x00
| AdjustedDecibels = 0x01

type NxtColourMode =
| Reflective = 0x00
| Ambient = 0x01
| Colour = 0x02
| Green = 0x03
| Blue = 0x04
| Raw = 0x05

type NxtUltrasonicMode =
| Centimeters = 0x00
| Inches = 0x01

type NxtTemperatureMode =
| Celsius = 0x00
| Fahrenheit = 0x01

type MoroMode =
| Degrees = 0x00
| Rotations = 0x01
| Percent = 0x02

type Ev3ColourMode = 
| Reflective = 0x00
| Ambient = 0x01
| Colour = 0x02
| ReflectiveRaw = 0x03
| ReflectiveRgb = 0x04
| Calibration = 0x05 // TODO?? ref orig

type Ev3UltrasonicMode =
| Centimeters = 0x00
| Inches = 0x01
| Listen = 0x02
| SiCentimeters = 0x03
| SiInches = 0x04
| DcCentimeters = 0x05
| DcInches = 0x06

type Ev3GyroscopeMode = 
| Angle = 0x00
| Rate = 0x01
| Fas = 0x02
| GandA = 0x03
| Calibrate = 0x04

type Ev3InfraredMode = 
| Proximity = 0x00
| Seek = 0x01
| Remote = 0x02
| RemoteA = 0x03
| SAlt = 0x04
| Calibrate = 0x05

type Ev3ColourSensorColours = 
| Transparent = 0x00
| Black = 0x01
| Blue = 0x02
| Green = 0x03
| Yellow = 0x04
| Red = 0x05
| White = 0x06
| Brown = 0x07