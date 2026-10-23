# A2DP Sink Router Privacy Policy / 隐私政策

## English

A2DP Sink Router privacy policy — effective 2026-10-23. Publisher: swahex. Support and privacy contact: [GitHub Issues](https://github.com/swahex/A2dpSinkRouter/issues).

**Audio processing.** The app receives audio from a phone paired through Windows Bluetooth and routes a user-selected recording endpoint to a selected playback endpoint. With “show all inputs,” other recording endpoints, including microphones, may be selected. Audio samples are captured, buffered, resampled when necessary and rendered locally in memory. The app does not implement audio recording files, speech transcription, audio uploads or cloud processing.

**Device information and settings.** The app enumerates paired phone audio sources and Windows audio endpoints. It reads device names, device/endpoint IDs, container IDs and availability/format information to match devices and create routes. Saved settings include selected phone/input/output IDs, per-phone route choices, prebuffer, volume, mute, language, appearance, reconnect-on-launch and close-to-tray preferences. It also attempts a read-only check of the selected input's Windows Listen setting; unavailable results do not prevent routing and the app does not change that setting.

**Diagnostic logs.** Local activity logs can include phone and endpoint names, input/output IDs, connection state, timestamps, errors, audio formats, buffering, volume/mute state and routing counters. Logs do not contain recorded audio samples. The application rotates the current log after it exceeds approximately 1 MB, retaining one previous log; settings remain until changed or deleted. Diagnostic values are not measurements of complete Bluetooth end-to-end latency. The optional `--package-check` diagnostic creates a local JSON with package/version/storage path, saved UI preferences, mutex availability, device counts and sanitized error codes; it does not connect a phone or start an audio stream.

**Network and sharing.** The app has no implemented internet service, account login, advertising, analytics, crash upload or telemetry client. It does not automatically send settings, logs, device identifiers or audio to the publisher or third parties. Bluetooth transport, pairing, drivers, Windows permissions, operating-system diagnostics and Microsoft Store distribution/update services are provided by Windows/Microsoft and follow their own terms and privacy controls. The absence of an application network client is not a claim that Windows never communicates over a network.

**Storage and control.** Portable ZIP builds store settings/logs in `%LOCALAPPDATA%\A2dpSinkRouter`. If you use an MSIX build, it uses the package's LocalCache directory under `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\A2dpSinkRouter`, excluded from Windows app-data backup/restore. “Preferences → Open data folder” opens the active location. MSIX updates normally preserve package data; ordinary uninstall or Windows app reset removes package-managed data. Portable files and any logs/backups you copied elsewhere remain until you delete them. No automatic transfer between portable and Store data is performed. Windows Bluetooth pairings remain managed by Windows.

Settings and logs are stored as local text files without application-level encryption. Access depends on Windows account and file permissions; someone with sufficient access to your Windows account or files may be able to read them. Protect your Windows account and any copies you export or share.

You can stop routing or exit from the tray to end application audio capture/playback. Review and redact device names, identifiers and any personal information in error messages before voluntarily sending logs through support channels. GitHub Issues are public: do not post sensitive information or unredacted logs. For a sensitive concern, first request a private contact without including sensitive details; for vulnerabilities, follow the repository's SECURITY.md instructions. GitHub handles information you submit through its service under its own terms and privacy policy. The application does not itself transmit those reports.

**Changes and contact.** Updates to this policy will be published with a revised effective date. Use [GitHub Issues](https://github.com/swahex/A2dpSinkRouter/issues) for privacy questions or requests concerning information you voluntarily sent to the publisher, requesting a private channel first if needed. This policy does not promise deletion of public third-party issue history or copies outside the publisher's control.

## 简体中文

A2DP Sink Router 隐私政策，生效日期：2026-10-23。发布者：swahex。支持与隐私联系入口：[GitHub Issues](https://github.com/swahex/A2dpSinkRouter/issues)。

**音频处理。** 应用接收经 Windows 蓝牙配对的手机音频，并将用户选定的录音端点转发到指定播放端点。“显示所有输入”可能允许选择麦克风等其他录音端点。音频采集、缓冲、必要的重采样和播放均在本机内存中进行。应用未实现保存录音文件、语音转写、上传音频或云端处理。

**设备信息与设置。** 应用枚举已配对的手机音源和 Windows 音频端点，读取设备名称、设备/端点 ID、容器 ID、可用状态及格式信息，以匹配设备并建立路由。保存的设置包括手机、输入和输出 ID、按手机记忆的路由、预缓冲、音量、静音、语言、主题、启动时连接与关闭到托盘偏好。应用还会尝试只读检查所选输入的 Windows“侦听”状态；无法读取时不阻止路由，不修改该设置。

**诊断日志。** 本地活动日志可能包含手机和音频端点名称、输入/输出 ID、连接状态、时间、错误、音频格式、缓冲参数、音量/静音状态及路由统计，不包含音频采样录音。当前日志超过约 1 MB 后轮换，保留一份旧日志；设置保留至用户更改或删除。统计值不是完整蓝牙端到端延迟测量。可选的 `--package-check` 诊断在本地生成 JSON，包含包/版本/数据路径、已保存的界面偏好、单实例锁可用性、设备数量及去除详细内容的错误码，不连接手机或启动音频流。

**联网与分享。** 应用未实现互联网服务、账号登录、广告、分析、崩溃上传或遥测客户端，不自动向发布者或第三方发送设置、日志、设备标识或音频。蓝牙传输、配对、驱动、Windows 权限、系统诊断及微软商店分发/更新由 Windows 或微软提供，适用各自条款和隐私控制。这不代表 Windows 本身不会联网。

**存储与控制。** 绿色 ZIP 版将设置和日志保存在 `%LOCALAPPDATA%\A2dpSinkRouter`。如果使用 MSIX 版本，应用使用包专属 LocalCache 目录，通常位于 `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\A2dpSinkRouter`，不纳入 Windows 应用数据备份/还原。“偏好 → 打开数据目录”可打开实际路径。MSIX 更新通常保留包数据；普通卸载或 Windows 应用重置会移除包管理的数据。绿色版文件及另行复制的日志/备份仍需自行删除。两个渠道不自动迁移数据，蓝牙配对继续由 Windows 管理。

设置和日志以本地文本文件保存，应用未对其进行额外加密。访问受 Windows 账号和文件权限控制；具有足够账号或文件访问权限的人可能读取这些内容。请保护你的 Windows 账号以及导出或分享的副本。

停止路由或从托盘退出可结束应用的音频采集和播放。自愿向支持渠道发送日志前，请检查并删除设备名称、标识符及错误信息中的个人内容。GitHub Issues 是公开的，请勿发布敏感信息或未脱敏日志。敏感问题请先请求私下联系方式，不要在请求中附带敏感详情；漏洞报告请遵循仓库 SECURITY.md 的说明。你通过 GitHub 提交的信息由 GitHub 按其自身条款及隐私政策处理。应用本身不发送这些报告。

**变更与联系。** 本政策更新时会修订生效日期。隐私问题或对自愿发送给发布者的信息提出请求，请通过 [GitHub Issues](https://github.com/swahex/A2dpSinkRouter/issues) 联系；如涉及敏感内容，请先请求私下沟通渠道。本政策不承诺删除第三方公开问题历史或发布者无法控制的副本。
