# Screenpresso 关键字命中（直接字符串证据）

说明：偏移是文件内字节偏移；ASCII 与 UTF-16LE 分别统计。每条记录最多保留 12 个命中样本。

## 框架与运行时

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| Qt5 | 2 | ASCII 0x00519AEB ?qT5}\n88????<br>ASCII 0x005A3E99 ?l???Qt5>?M?`???????w(??X? |
| VCL | 1 | ASCII 0x00651F1C ??VCLv?e??{ |
| TForm | 36 | ASCII 0x002D6E5B PlatformID<br>ASCII 0x002DF39F PDXVAHDSW_GetVideoProcessorOutputFormats<br>ASCII 0x002DF3C8 PDXVAHDSW_GetVideoProcessorInputFormats<br>ASCII 0x002EDB75 MF_E_PLATFORM_NOT_INITIALIZED<br>ASCII 0x002FA7C2 InputFormatCaps<br>ASCII 0x002FA7D2 OutputFormatCount<br>ASCII 0x002FA7E4 InputFormatCount<br>ASCII 0x002FAA3C GetVideoProcessorOutputFormats<br>ASCII 0x002FAA5B GetVideoProcessorInputFormats<br>ASCII 0x002FAC39 OutputFormat<br>ASCII 0x002FAC6F InputFormat<br>ASCII 0x0030E22E inputFormat |
| TObject | 14 | ASCII 0x002DA293 NetObjectOptions<br>ASCII 0x0031373D GetObject<br>ASCII 0x003137CA SelectObject<br>ASCII 0x003137D7 GetCurrentObject<br>ASCII 0x003156CF ReadNetObject<br>ASCII 0x003156DD WriteNetObject<br>ASCII 0x0031573E System.Runtime.Serialization.ISerializable.GetObjectData<br>ASCII 0x00316043 SetRootObject<br>ASCII 0x003163BE WriteStartObject<br>ASCII 0x003163E2 IsStartObject<br>ASCII 0x00317A2D GetObjectData<br>ASCII 0x00321630 SetObject |
| mscoree | 1 | ASCII 0x006BCEEA mscoree.dll |
| System\.Windows\.Forms | 5 | ASCII 0x002D8C9E System.Windows.Forms<br>ASCII 0x003A8BBF rSystem.Windows.Forms.Keys, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089<br>ASCII 0x003A8C43 rSystem.Windows.Forms.Keys, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089<br>ASCII 0x003A8CBE rSystem.Windows.Forms.Keys, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089<br>ASCII 0x003A9007 UySystem.Windows.Forms.Orientation, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089 |
| PresentationFramework | 6 | ASCII 0x0034ACEF PresentationFramework<br>ASCII 0x0039B265 TypeySystem.Windows.Controls.Control, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35??<br>ASCII 0x0039B303 TypeySystem.Windows.Controls.Control, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35??<br>ASCII 0x0039B39D Type??System.Windows.Controls.Primitives.Thumb, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35??<br>ASCII 0x0039B440 TypeySystem.Windows.FrameworkElement, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35??<br>ASCII 0x0039B4DA TypeySystem.Windows.FrameworkElement, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35O |
| CEF | 48 | ASCII 0x002D64A8 UltimateResourceFallbackLocation<br>ASCII 0x002E0E22 AMPushSourceFlags<br>ASCII 0x002E1619 IFileSourceFilter<br>ASCII 0x002F3591 PKEY_AudioEngine_DeviceFormat<br>ASCII 0x002F92DC InterlaceFlags<br>ASCII 0x00300E80 E_CannotLoadSourceFilter<br>ASCII 0x00301721 VMRSurfaceFlipped<br>ASCII 0x0030227B BDASourceFiltersCategory<br>ASCII 0x003035A1 dwInterlaceFlags<br>ASCII 0x00309390 58E8142CDDE695FFE119E16D03B99D32AFA9CEF767F293D16F9C8D527DCE4519<br>ASCII 0x00309D36 6E9AE95ACA02DF626FA33A9741718E9A2C4E3B2FCEFBBBD19323E7F8C82C99E5<br>ASCII 0x0030B104 9CEF436DFAEC5D0FF26734AA2AE76EBFAC739240E0A4607387D14F8263B197FA |
| Electron | 1 | ASCII 0x002EBFA1 AttMicroelectronics |
| System\.Reflection | 2 | ASCII 0x002D657F System.Reflection<br>ASCII 0x002D71B7 System.Reflection.Emit |

## 截图采集

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| BitBlt | 2 | ASCII 0x002D9749 BitBltRop<br>ASCII 0x0031370B BitBlt |
| GetDC | 1 | ASCII 0x00313AA6 GetDC |
| CreateCompatibleBitmap | 1 | ASCII 0x0031375E CreateCompatibleBitmap |
| CreateCompatibleDC | 1 | ASCII 0x0031378A CreateCompatibleDC |
| GetDIBits | 1 | ASCII 0x00313747 GetDIBits |
| DWM | 38 | ASCII 0x002D972E DwmWindowAttribute<br>ASCII 0x002FEE7D dwMerit<br>ASCII 0x002FEEEE dwMediaSampleContent<br>ASCII 0x00301EEE dwMinNumSurfaces<br>ASCII 0x00301EFF dwMaxNumSurfaces<br>ASCII 0x00302F98 dwMicrosecondsPerField<br>ASCII 0x003035C1 dwMinBuffers<br>ASCII 0x003035CE dwMaxBuffers<br>ASCII 0x003136AC DwmIsCompositionEnabled<br>ASCII 0x003136C4 DwmGetWindowAttribute<br>ASCII 0x003136DA DwmSetWindowAttribute<br>ASCII 0x003387AB dwMixerIDFlags |
| MonitorFromPoint | 1 | ASCII 0x00313D7C MonitorFromPoint |
| GetForegroundWindow | 1 | ASCII 0x00313C2D GetForegroundWindow |
| GetWindowRect | 2 | ASCII 0x003136F0 GetWindowRect2<br>ASCII 0x00313A43 GetWindowRect |

## 录屏与编码

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| H264 | 8 | ASCII 0x001584DE  H264<br>ASCII 0x002DDED7 eAVEncH264VProfile<br>ASCII 0x002F6872 H264<br>ASCII 0x00305BD6 H264_GENERIC_FILM<br>ASCII 0x00305BEF H264_LOG_100_TO_1<br>ASCII 0x00305C01 H264_LOG_316_TO_1<br>ASCII 0x00305C13 H264_RGB<br>ASCII 0x00305C20 H264_YCgCo |
| h264 | 8 | ASCII 0x001584DE  H264<br>ASCII 0x002DDED7 eAVEncH264VProfile<br>ASCII 0x002F6872 H264<br>ASCII 0x00305BD6 H264_GENERIC_FILM<br>ASCII 0x00305BEF H264_LOG_100_TO_1<br>ASCII 0x00305C01 H264_LOG_316_TO_1<br>ASCII 0x00305C13 H264_RGB<br>ASCII 0x00305C20 H264_YCgCo |
| MFVideoFormat | 4 | ASCII 0x002DE087 MFVideoFormat<br>ASCII 0x0031FF26 MFCreateMFVideoFormatFromMFMediaType<br>ASCII 0x0031FF68 MFInitMediaTypeFromMFVideoFormat<br>ASCII 0x0039DB0B MFVIDEOFORMAT |
| MFCreateSinkWriterFromURL | 1 | ASCII 0x0031B8F0 MFCreateSinkWriterFromURL |
| SinkWriter | 6 | ASCII 0x002DB8B6 IMFSinkWriter<br>ASCII 0x002DE963 IMFSinkWriterCallback<br>ASCII 0x002F6614 CLSID_MFSinkWriter<br>ASCII 0x0031B8F0 MFCreateSinkWriterFromURL<br>ASCII 0x0032035B MFCreateSinkWriterFromMediaSink<br>ASCII 0x003391D8 ppSinkWriter |
| MediaFoundation | 36 | ASCII 0x002DB804 NAudio.MediaFoundation<br>ASCII 0x002DB906 MediaFoundationAttributes<br>ASCII 0x002DB920 MediaFoundationErrors<br>ASCII 0x002DB936 MediaFoundationApi<br>ASCII 0x002DB949 MediaFoundationInterop<br>ASCII 0x002DB960 MediaFoundationTransform<br>ASCII 0x002DB979 MediaFoundationTransformCategories<br>ASCII 0x002DBFD0 MediaFoundationEncoder<br>ASCII 0x002DC071 MediaFoundationResampler<br>ASCII 0x002DC1ED MediaFoundationReader<br>ASCII 0x002DC25E StreamMediaFoundationReader<br>ASCII 0x002DD320 MediaFoundation |
| MFStartup | 2 | ASCII 0x002DD741 MFStartup<br>ASCII 0x0039C969 MFSTARTUP_* defines |
| AVIF | 11 | ASCII 0x002D9A77 AviFileMode<br>ASCII 0x002D9AEF AviFileInfo<br>ASCII 0x00335DFD aviFile<br>ASCII 0x0034A345 avifil32.dll<br>ASCII 0x0034A41B AVIFileInit<br>ASCII 0x0034A427 AVIFileOpen<br>ASCII 0x0034A449 AVIFileGetStream<br>ASCII 0x0034A4AF AVIFileCreateStream<br>ASCII 0x0034A58C AVIFileRelease<br>ASCII 0x0034A59B AVIFileExit<br>ASCII 0x0034A5E1 AVIFileInfo |
| VP8 | 1 | ASCII 0x0067542B `?1=?U?*???5???vp8j4?*}?c??)??? |
| VP9 | 1 | ASCII 0x005D56BE {VP9???｜??* |
| MPEG4 | 5 | ASCII 0x002F5130 MF_MT_MPEG4_SAMPLE_DESCRIPTION<br>ASCII 0x002F514F MF_MT_MPEG4_CURRENT_SAMPLE_ENTRY<br>ASCII 0x002F57D9 MPEG4<br>ASCII 0x002F6768 MFMPEG4Format<br>ASCII 0x003208E5 MFCreateMPEG4MediaSink |
| mp4 | 4 | ASCII 0x002F6819 MP43<br>ASCII 0x002F681E MP4S<br>ASCII 0x002F6828 MP4V<br>ASCII 0x002FDF9F EDAM_MIME_TYPE_MP4_VIDEO |
| webm | 1 | ASCII 0x002EA78B WebM |
| gif | 8 | ASCII 0x002F12CE DigiFix<br>ASCII 0x002FCFFE _enableGiftSubscriptions<br>ASCII 0x002FDF2C EDAM_MIME_TYPE_GIF<br>ASCII 0x002FE03F EDAM_COMMERCE_SERVICE_GIFT<br>ASCII 0x0030F14F enableGiftSubscriptions<br>ASCII 0x00327686 get_EnableGiftSubscriptions<br>ASCII 0x003276A2 set_EnableGiftSubscriptions<br>ASCII 0x00349036 EnableGiftSubscriptions |
| GIF | 8 | ASCII 0x002F12CE DigiFix<br>ASCII 0x002FCFFE _enableGiftSubscriptions<br>ASCII 0x002FDF2C EDAM_MIME_TYPE_GIF<br>ASCII 0x002FE03F EDAM_COMMERCE_SERVICE_GIFT<br>ASCII 0x0030F14F enableGiftSubscriptions<br>ASCII 0x00327686 get_EnableGiftSubscriptions<br>ASCII 0x003276A2 set_EnableGiftSubscriptions<br>ASCII 0x00349036 EnableGiftSubscriptions |
| AVI | 102 | ASCII 0x002D8E7F IOperationBehavior<br>ASCII 0x002D8F40 BehaviorExtensionElement<br>ASCII 0x002D8F7B IEndpointBehavior<br>ASCII 0x002D8FBE DataContractSerializerOperationBehavior<br>ASCII 0x002D9A63 AviProgressCallback<br>ASCII 0x002D9A77 AviFileMode<br>ASCII 0x002D9A83 AviCompressOptionsFlags<br>ASCII 0x002D9A9B AVIStreamWriteFlags<br>ASCII 0x002D9AAF AVISaveOptionsFlags<br>ASCII 0x002D9AE1 AviStreamInfo<br>ASCII 0x002D9AEF AviFileInfo<br>ASCII 0x002D9AFB AviCompressOptions |
| WMV | 5 | ASCII 0x002F682D WMV1<br>ASCII 0x002F6832 WMV2<br>ASCII 0x002F6837 WMV3<br>ASCII 0x003206FB MFCreateWMVEncoderActivate<br>ASCII 0x00661640 FU&???AK]3???6?M??a???Y"wMV???? |
| AAC | 27 | ASCII 0x002ECB35 MFAudioFormat_AAC<br>ASCII 0x002ECC78 MEDIASUBTYPE_RAW_AAC1<br>ASCII 0x002EDB35 MF_MT_AAC_PAYLOAD_TYPE<br>ASCII 0x002EDB4C MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION<br>ASCII 0x002F1824 RawAac<br>ASCII 0x002F1CEC MPEG_ADTS_AAC<br>ASCII 0x002F1CFA MPEG_RAW_AAC<br>ASCII 0x002F1D11 NOKIA_MPEG_ADTS_AAC<br>ASCII 0x002F1D25 NOKIA_MPEG_RAW_AAC<br>ASCII 0x002F1D38 VODAFONE_MPEG_ADTS_AAC<br>ASCII 0x002F1D4F VODAFONE_MPEG_RAW_AAC<br>ASCII 0x002F1D65 MPEG_HEAAC |
| MP3 | 24 | ASCII 0x002DBC89 DmoMp3FrameDecompressor<br>ASCII 0x002DBCA1 NAudio.FileFormats.Mp3<br>ASCII 0x002DBCD9 IMp3FrameDecompressor<br>ASCII 0x002DBCEF Mp3Frame<br>ASCII 0x002DBCF8 AcmMp3FrameDecompressor<br>ASCII 0x002DBE2D Mp3WaveFormat<br>ASCII 0x002DBE3B Mp3WaveFormatFlags<br>ASCII 0x002DBE4E Mp3WaveFormatId<br>ASCII 0x002DC208 Mp3FileReader<br>ASCII 0x002DCA40 WindowsMediaMp3Decoder<br>ASCII 0x002ECBB0 MFAudioFormat_MP3<br>ASCII 0x002EE422 MF_E_MP3_NOTFOUND |
| Opus | 2 | ASCII 0x002EBCF7 Canopus<br>ASCII 0x002F1512 WAVE_FORMAT_CANOPUS_ATRAC |
| PCM | 52 | ASCII 0x002D9AD3 PcmWaveFormat<br>ASCII 0x002DBDF9 AdpcmWaveFormat<br>ASCII 0x002DBE1A ImaAdpcmWaveFormat<br>ASCII 0x002DC32C PCM=<br>ASCII 0x002DC4F5 Pcm16BitToSampleProvider<br>ASCII 0x002DC50E Pcm24BitToSampleProvider<br>ASCII 0x002DC527 Pcm32BitToSampleProvider<br>ASCII 0x002DC540 Pcm8BitToSampleProvider<br>ASCII 0x002ECBE8 MFAudioFormat_PCM<br>ASCII 0x002ECCD4 KSDATAFORMAT_SUBTYPE_ADPCM<br>ASCII 0x002ECD36 ImaAdpcm<br>ASCII 0x002F1253 Adpcm |
| WASAPI | 3 | ASCII 0x002DBEE1 WasapiLoopbackCapture<br>ASCII 0x002DC006 WasapiOut<br>ASCII 0x002DCEEB WasapiCapture |
| waveIn | 31 | ASCII 0x002DBD80 WaveInCapabilities<br>ASCII 0x002DBD98 WaveInEventArgs<br>ASCII 0x002DBED9 IWaveIn<br>ASCII 0x002DBEF7 WaveIn<br>ASCII 0x002DBEFE WaveInEvent<br>ASCII 0x002DC137 WaveInProvider<br>ASCII 0x002E5AA2 WaveInOutOpenFlags<br>ASCII 0x002EC524 WaveInHandle<br>ASCII 0x002EC6B7 DestinationWaveIn<br>ASCII 0x0030E0E9 WaveInOpen<br>ASCII 0x0030E0F4 WaveInClose<br>ASCII 0x0030E100 WaveInData |
| waveOut | 27 | ASCII 0x002DBDAD WaveOutCapabilities<br>ASCII 0x002DC02A WaveOut<br>ASCII 0x002DC032 WaveOutEvent<br>ASCII 0x002EC516 WaveOutHandle<br>ASCII 0x002EC75A SourceWaveOut<br>ASCII 0x0030E10B WaveOutClose<br>ASCII 0x0030E118 WaveOutDone<br>ASCII 0x0030E124 WaveOutOpen<br>ASCII 0x003393A6 waveOutHandle<br>ASCII 0x003393C1 hWaveOut<br>ASCII 0x003393CA lpWaveOutHdr<br>ASCII 0x003393F0 waveOutCaps |
| DirectShow | 14 | ASCII 0x002E0BA0 DirectShowLib<br>ASCII 0x002E2775 DirectShowPluginControl<br>ASCII 0x002E2E91 DirectShowLib.SBE<br>ASCII 0x002E3152 DirectShowLib.DMO<br>ASCII 0x002E3352 DirectShowLib.Dvd<br>ASCII 0x002E370C DirectShowLib.DES<br>ASCII 0x002E3A96 DirectShowLib.BDA<br>ASCII 0x002E5665 DirectShowLib.MultimediaStreaming<br>ASCII 0x00398D51 DirectShowLib.EMTMarshaler<br>ASCII 0x00398D7C DirectShowLib.SBE.SDMarshaler<br>ASCII 0x00398DA3 DirectShowLib.DTAMarshaler<br>ASCII 0x00398DC3 DirectShowLib.DKAMarshaler |
| ICodec | 4 | ASCII 0x002E14EB ICodecAPI<br>ASCII 0x0030246A AMKSVBICodec<br>ASCII 0x00302562 ICodecAPIProxy<br>ASCII 0x0039F951 ??This interface is deprecated and is maintained for backward compatibility only. New applications and drivers should use the ICodecAPI inte… |
| VideoCodec | 2 | ASCII 0x002E2544 DVVideoCodec<br>ASCII 0x002E25F9 CMpegVideoCodec |

## 快捷键与热键

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| RegisterHotKey | 2 | ASCII 0x00313A86 RegisterHotKey<br>ASCII 0x00313A95 UnregisterHotKey |
| UnregisterHotKey | 1 | ASCII 0x00313A95 UnregisterHotKey |
| SetWindowsHookEx | 2 | ASCII 0x00313D02 SetWindowsHookEx<br>ASCII 0x00313D36 SetWindowsHookEx2 |
| PrintScreen | 1 | ASCII 0x003A8C33 PrintScreen |
| HotKey | 7 | ASCII 0x002D92E1 HotKeyDel<br>ASCII 0x002E821B WM_HOTKEY<br>ASCII 0x00313A86 RegisterHotKey<br>ASCII 0x00313A95 UnregisterHotKey<br>ASCII 0x00313ECA get_HotKey<br>ASCII 0x00313ED5 set_HotKey<br>ASCII 0x00345D56 HotKey |
| MOD_ALT | 1 | ASCII 0x002E8272 MOD_ALT |
| MOD_SHIFT | 1 | ASCII 0x002E8283 MOD_SHIFT |
| LowLevelKeyboardProc | 1 | ASCII 0x002D9919 LowLevelKeyboardProc |
| WH_KEYBOARD_LL | 1 | ASCII 0x002E8512 WH_KEYBOARD_LL |

## 剪贴板与拖拽

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| SetClipboardData | 1 | ASCII 0x00313683 SetClipboardData |
| OpenClipboard | 1 | ASCII 0x0031364E OpenClipboard |
| EmptyClipboard | 1 | ASCII 0x0031363F EmptyClipboard |
| CF_DIB | 2 | ASCII 0x002E8E80 CF_DIB<br>ASCII 0x002E8E9F CF_DIBV5 |
| CF_HDROP | 1 | ASCII 0x002E8E96 CF_HDROP |
| CF_UNICODETEXT | 1 | ASCII 0x002E8E87 CF_UNICODETEXT |

## 系统集成

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| Startup | 12 | ASCII 0x002D7C88 StartupEventArgs<br>ASCII 0x002D7F03 WindowStartupLocation<br>ASCII 0x002D9C36 <OnStartup>d__8<br>ASCII 0x002DD741 MFStartup<br>ASCII 0x002EDF3D MF_E_BAD_STARTUP_VERSION<br>ASCII 0x00314971 OnStartup<br>ASCII 0x003149AA <OnStartup>b__8_0<br>ASCII 0x003149BC <OnStartup>b__8_1<br>ASCII 0x0031B7EA Startup<br>ASCII 0x003449FD set_WindowStartupLocation<br>ASCII 0x0039C969 MFSTARTUP_* defines<br>ASCII 0x003A7438 2LearnPulse.ScreenpressoCapture.App+<OnStartup>d__8 |
| Mutex | 12 | ASCII 0x002D6712 Mutex<br>ASCII 0x00321F54 GetOutputMutexCount<br>ASCII 0x00321F68 GetOutputMutex<br>ASCII 0x00321F77 SetOutputMutexSelection<br>ASCII 0x0033C291 pcMutexs<br>ASCII 0x0033C29A dwMutexIndex<br>ASCII 0x0033C2A7 ppIMutex<br>ASCII 0x0033C2B0 pIMutex<br>ASCII 0x0033C3FB pcMutexes<br>ASCII 0x0033C405 dwMutexNum<br>ASCII 0x0033C410 ppMutex<br>ASCII 0x00343456 ReleaseMutex |
| DDE | 88 | ASCII 0x002D692B DebuggerHiddenAttribute<br>ASCII 0x002D92C7 DataReceivedDel<br>ASCII 0x002D9C95 EmbeddedAttribute<br>ASCII 0x002DA221 TypeAddedEventArgs<br>ASCII 0x002DA9FA PDF417CodewordDecoder<br>ASCII 0x002DACD4 AbstractExpandedDecoder<br>ASCII 0x002DB49E FieldDescriptionAttribute<br>ASCII 0x002DB4B8 FieldDescriptionHelper<br>ASCII 0x002DBF76 DirectSoundDeviceInfo<br>ASCII 0x002DEE75 IDirect3DDevice9Ex<br>ASCII 0x002DF7B1 AndroidDebugBridge<br>ASCII 0x002DF7C4 AndroidDebugBridgeEventArgs |

## 编辑与标注

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| Arrow | 5 | ASCII 0x002E935D IDC_ARROW<br>ASCII 0x002E9384 IDC_UPARROW<br>ASCII 0x00314FA3 UpdateScrollArrow<br>ASCII 0x00337A08 noNarrow<br>ASCII 0x00344D83 get_Arrow |
| Rectangle | 18 | ASCII 0x002D8193 RectangleGeometry<br>ASCII 0x002D896E Rectangle<br>ASCII 0x002D8A27 RectangleF<br>ASCII 0x002DB09A MonochromeRectangleDetector<br>ASCII 0x002DB0B6 WhiteRectangleDetector<br>ASCII 0x002EB5C2 FORCE_RECTANGLE<br>ASCII 0x0031972C getEnclosingRectangle<br>ASCII 0x0032238F ToRectangle<br>ASCII 0x0032239B FromRectangle<br>ASCII 0x0032B0C0 ToRectangleF<br>ASCII 0x00335D56 rectangle<br>ASCII 0x003452B1 FillRectangle |
| Ellipse | 1 | ASCII 0x003452EE DrawEllipse |
| Magnifier | 12 | ASCII 0x002D94E2 Magnifier<br>ASCII 0x003144CC get_DisableMagnifier<br>ASCII 0x003144E1 set_DisableMagnifier<br>ASCII 0x00314E4A get_MagnifierVisibility<br>ASCII 0x00314E62 get_MagnifierBounds<br>ASCII 0x00314E76 set_MagnifierBounds<br>ASCII 0x00314E8A get_MagnifierViewBox<br>ASCII 0x00314E9F set_MagnifierViewBox<br>ASCII 0x00345E33 DisableMagnifier<br>ASCII 0x003461E1 MagnifierVisibility<br>ASCII 0x003461F5 MagnifierBounds<br>ASCII 0x00346205 MagnifierViewBox |
| Watermark | 2 | ASCII 0x0032BEBC get_WatermarkURL<br>ASCII 0x0033FB74 pbstrWatermarkURL |
| Rotate | 9 | ASCII 0x00317D1F get_AutoRotate<br>ASCII 0x00317D2E set_AutoRotate<br>ASCII 0x00317E41 rotateCounterClockwise<br>ASCII 0x00317E58 rotateCounterClockwise45<br>ASCII 0x00317E71 get_RotateSupported<br>ASCII 0x00319722 rotate180<br>ASCII 0x0032441A GetRotated<br>ASCII 0x003468CB AutoRotate<br>ASCII 0x003468F2 RotateSupported |
| Flip | 7 | ASCII 0x002E8F52 Flip3DPolicy<br>ASCII 0x002FF454 FlipHorizontal<br>ASCII 0x002FF463 FlipVertical<br>ASCII 0x00301721 VMRSurfaceFlipped<br>ASCII 0x00303594 DirectedFlip<br>ASCII 0x00319685 flip<br>ASCII 0x0033F5AE dwFlipToIndex |
| Crop | 12 | ASCII 0x002D8309 CroppedBitmap<br>ASCII 0x002EC705 SourceMicrophone<br>ASCII 0x003004D7 MinCroppingSize<br>ASCII 0x003004E7 MaxCroppingSize<br>ASCII 0x003004F7 CropGranularityX<br>ASCII 0x00300508 CropGranularityY<br>ASCII 0x00300519 CropAlignX<br>ASCII 0x00300524 CropAlignY<br>ASCII 0x00301BB6 Crop<br>ASCII 0x00317E85 crop<br>ASCII 0x00317E8A get_CropSupported<br>ASCII 0x00346902 CropSupported |
| Resize | 9 | ASCII 0x002D9C5E AreaResizeType<br>ASCII 0x002E3873 ResizeFlags<br>ASCII 0x002E3A7E IResize<br>ASCII 0x002F2621 StructureSize<br>ASCII 0x002F6E02 cbSignatureSize<br>ASCII 0x0031BBA6 OnResize<br>ASCII 0x0032E5B4 SetResizerGUID<br>ASCII 0x00340F3C ResizerGuid<br>ASCII 0x003442D1 Resize |
| Stamp | 38 | ASCII 0x002E18FA IAMAsyncReaderTimestampScaling<br>ASCII 0x002E95A4 TimeStamp<br>ASCII 0x002ED820 MFT_HW_TIMESTAMP_WITH_QPC_Attribute<br>ASCII 0x002EDCBA MF_E_INVALID_TIMESTAMP<br>ASCII 0x002EDD27 MF_E_NO_SAMPLE_TIMESTAMP<br>ASCII 0x002EFFC3 llLastTimestampReceived<br>ASCII 0x002EFFDB llLastTimestampEncoded<br>ASCII 0x002EFFF2 llLastTimestampProcessed<br>ASCII 0x002F31C2 TimestampError<br>ASCII 0x002F55D1 MFSampleExtension_DeviceTimestamp<br>ASCII 0x002FD6F7 _timestamp<br>ASCII 0x00301977 DvdVOBU_Timestamp |
| Effects | 14 | ASCII 0x002ECE16 MF_SOURCE_READERF_ALLEFFECTSREMOVED<br>ASCII 0x002F0784 ChorusEffectsSend<br>ASCII 0x002F0796 ReverbEffectsSend<br>ASCII 0x002F332A SoundEffects<br>ASCII 0x002F3337 GameEffects<br>ASCII 0x00302331 VideoEffects1Category<br>ASCII 0x00302347 VideoEffects2Category<br>ASCII 0x0030235D AudioEffects1Category<br>ASCII 0x00302373 AudioEffects2Category<br>ASCII 0x00304B86 Effects<br>ASCII 0x003225FA RemoveAllEffects<br>ASCII 0x0032DAC6 EnableEffects |

## OCR 与识别

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| OCR | 8 | ASCII 0x002E7367 ASIOcreateBuffers<br>ASCII 0x00301F52 dwWidthToCreate<br>ASCII 0x00301F62 dwHeightToCreate<br>ASCII 0x003369BA noAutoCreate<br>ASCII 0x003369FB autoCreate<br>ASCII 0x00342599 pHrCoCreateRetVal<br>ASCII 0x0034AC51 CoCreateInstance<br>ASCII 0x004E5642 Ocr?x?y?u5?w?s???Vr????cM?3t.?TJ?_ |
| Barcode | 32 | ASCII 0x002DA50E BarcodeFormat<br>ASCII 0x002DA522 BarcodeReader<br>ASCII 0x002DA530 BarcodeReaderGeneric`1<br>ASCII 0x002DA547 BarcodeWriter<br>ASCII 0x002DA555 BarcodeWriterSvg<br>ASCII 0x002DA566 BarcodeWriterGeneric`1<br>ASCII 0x002DA602 IBarcodeReader<br>ASCII 0x002DA611 IBarcodeReaderGeneric`1<br>ASCII 0x002DA629 IBarcodeWriter<br>ASCII 0x002DA638 IBarcodeWriterGeneric`1<br>ASCII 0x002DA650 IMultipleBarcodeReader<br>ASCII 0x002DA667 IMultipleBarcodeReaderGeneric`1 |
| ZXing | 25 | ASCII 0x002DA51C ZXing<br>ASCII 0x002DA7AB ZXing.Rendering<br>ASCII 0x002DA7F0 ZXing.QrCode<br>ASCII 0x002DA838 ZXing.QrCode.Internal<br>ASCII 0x002DA91F ZXing.PDF417<br>ASCII 0x002DA975 ZXing.PDF417.Internal<br>ASCII 0x002DAA95 ZXing.PDF417.Internal.EC<br>ASCII 0x002DAACB ZXing.OneD<br>ASCII 0x002DAC5A ZXing.OneD.RSS<br>ASCII 0x002DACBC ZXing.OneD.RSS.Expanded<br>ASCII 0x002DACEC ZXing.OneD.RSS.Expanded.Decoders<br>ASCII 0x002DAD7D ZXing.Multi |
| QRCoder | 1 | ASCII 0x002DA7FD QRCodeReader |
| QRCode | 10 | ASCII 0x002DA7DA QrCodeEncodingOptions<br>ASCII 0x002DA7F0 ZXing.QrCode<br>ASCII 0x002DA7FD QRCodeReader<br>ASCII 0x002DA80A QRCodeWriter<br>ASCII 0x002DA838 ZXing.QrCode.Internal<br>ASCII 0x002DA86D QRCodeDecoderMetaData<br>ASCII 0x002DA902 QRCode<br>ASCII 0x002DADBC QRCodeMultiReader<br>ASCII 0x002DADCE ZXing.Multi.QrCode<br>ASCII 0x002DADEF ZXing.Multi.QrCode.Internal |
| zlib | 1 | ASCII 0x0033D496 noZlibHeaderOrFooter |
| WebP | 2 | ASCII 0x002D880A IWebProxy<br>ASCII 0x002F5BDF MFNETSOURCE_BROWSERWEBPAGE |

## 云与分享

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| Screenpresso | 16 | ASCII 0x002D6399 ScreenpressoCapture.exe<br>ASCII 0x002D93B0 LearnPulse.Screenpresso<br>ASCII 0x002D93D7 ScreenpressoService<br>ASCII 0x002D941C LearnPulse.ScreenpressoCapture<br>ASCII 0x002D94B1 ScreenpressoCaptureSerializer<br>ASCII 0x0034ACD2 ScreenpressoCapture<br>ASCII 0x003A7438 2LearnPulse.ScreenpressoCapture.App+<OnStartup>d__8<br>ASCII 0x003A7494 CLearnPulse.ScreenpressoCapture.RegionWindow+<<OnMouseMove>b__23_0>d<br>ASCII 0x003A8B78 0LearnPulse.ScreenpressoCapture.VideoRecorderType<br>ASCII 0x003A925E Screenpresso<br>ASCII 0x003A9270 ScreenpressoCapture<br>ASCII 0x006BCF54 C:\git\screenpresso\ScreenpressoCapture\src\obj\Release\net47\ScreenpressoCapture.pdb |
| Learnpulse | 33 | ASCII 0x002D9235 LearnPulse<br>ASCII 0x002D9282 LearnPulse.XFormPulse<br>ASCII 0x002D932F LearnPulse.XThirdParties<br>ASCII 0x002D9355 LearnPulse.XLicensePulse<br>ASCII 0x002D93B0 LearnPulse.Screenpresso<br>ASCII 0x002D941C LearnPulse.ScreenpressoCapture<br>ASCII 0x002DFD1D LearnPulse.XThirdParties.Zip<br>ASCII 0x002DFF7D LearnPulse.XThirdParties.Zip.Compression<br>ASCII 0x002E0041 LearnPulse.XThirdParties.Zip.Compression.Streams<br>ASCII 0x002E00D0 LearnPulse.XThirdParties.Tar<br>ASCII 0x002E0166 LearnPulse.XThirdParties.GZip<br>ASCII 0x002E01C0 LearnPulse.XThirdParties.Encryption |
| Google | 4 | ASCII 0x002FE005 EDAM_COMMERCE_SERVICE_GOOGLE<br>ASCII 0x00319E73 get_GoogleMapsURI<br>ASCII 0x00319E85 set_GoogleMapsURI<br>ASCII 0x00346F38 GoogleMapsURI |
| Drive | 68 | ASCII 0x002D6A83 DriveInfo<br>ASCII 0x002D6CB1 DriveType<br>ASCII 0x002DC685 AcmDriver<br>ASCII 0x002DC6B6 AcmDriverDetailsSupportFlags<br>ASCII 0x002DC787 AsioDriver<br>ASCII 0x002DC792 AsioDriverCapability<br>ASCII 0x002DC7BE AsioDriverExt<br>ASCII 0x002E5BFE AcmDriverEnumCallback<br>ASCII 0x002E7260 ASIOgetDriverName<br>ASCII 0x002E7272 ASIOgetDriverVersion<br>ASCII 0x002E85E1 LicenseBoundToHardDrive<br>ASCII 0x002E860A LicenseBoundToHardDrive2 |
| YouTube | 7 | ASCII 0x002DA3EC YoutubeExtractor<br>ASCII 0x002DA4C3 YoutubeParseException<br>ASCII 0x00317740 TryNormalizeYoutubeUrl<br>ASCII 0x00331FD9 System.Collections.Generic.IEnumerator<YoutubeExtractor.DownloadUrlResolver.ExtractionInfo>.get_Current<br>ASCII 0x00332041 System.Collections.Generic.IEnumerable<YoutubeExtractor.DownloadUrlResolver.ExtractionInfo>.GetEnumerator<br>ASCII 0x00349C30 System.Collections.Generic.IEnumerator<YoutubeExtractor.DownloadUrlResolver.ExtractionInfo>.Current<br>ASCII 0x003A75F3 >YoutubeExtractor.DownloadUrlResolver+<ExtractDownloadUrls>d__5 |
| S3 | 125 | ASCII 0x000235E1 ??s3<br>ASCII 0x000290A1 Yls3<br>ASCII 0x0002A3DC X@s3<br>ASCII 0x00101B5C +?s3M<br>ASCII 0x0015778A  S340<br>ASCII 0x001577B2  S342<br>ASCII 0x0016E34D 8p???s3S<br>ASCII 0x0016FE4D 8p???s3T<br>ASCII 0x00171A22 8K???s3U<br>ASCII 0x001B3526 s3??<br>ASCII 0x002D96B3 <>c__DisplayClass35_0<br>ASCII 0x002D96C9 <>c__DisplayClass36_0 |
| FTP | 4 | ASCII 0x002DE7BA MFTProcessOutputFlags<br>ASCII 0x002EC997 SoftPedal<br>ASCII 0x003379BB leftPair<br>ASCII 0x0044235E ???+??G??=oFTP??0???! |
| Upload | 38 | ASCII 0x002E059C FacebookUploadProgressChangedEventArgs<br>ASCII 0x002F3A6E Upload<br>ASCII 0x002FC9FD UploadProgressChanged<br>ASCII 0x002FD123 _canPurchaseUploadAllowance<br>ASCII 0x002FD1E2 _uploadLimit<br>ASCII 0x002FD1EF _uploadLimitEnd<br>ASCII 0x002FD1FF _uploadLimitNextMonth<br>ASCII 0x002FDDA2 _uploaded<br>ASCII 0x002FE6A9 EDAM_USER_UPLOAD_LIMIT_FREE<br>ASCII 0x002FE6C5 EDAM_USER_UPLOAD_LIMIT_PREMIUM<br>ASCII 0x0030EF5D notifyUploadProgressChanged<br>ASCII 0x0030F242 canPurchaseUploadAllowance |

## 文件格式与导出

| 关键字 | 命中总数 | 样本偏移与文本 |
| --- | --- | --- |
| PNG | 8 | ASCII 0x002D8289 PngBitma