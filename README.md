# ValSync Nile

ValSync Nile unblocks websites and game voice chat on Egyptian ISPs: WE (TE Data), Vodafone, Orange and Etisalat/e&. It's a one-click Windows package built on two open-source engines that run side by side:

- **Websites (TCP):** the [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) 0.2.2 engine. Egyptian ISPs block sites by reading the domain name (SNI) in the TLS handshake and by tampering with DNS answers. GoodbyeDPI splits the TLS ClientHello and sends fake packets so the DPI box can't read the domain. DNS goes to Yandex on port 1253, a non-standard port the ISP doesn't intercept.
- **Voice chat in games and calls (UDP):** the [zapret](https://github.com/bol-van/zapret) `winws` v72.13 engine. It covers Valorant/Vivox, Discord voice, WhatsApp and Telegram calls (STUN), and other UDP voice. GoodbyeDPI can't handle UDP, so voice needs this separate process. It only touches UDP, and GoodbyeDPI only touches TCP and DNS, so the two never handle the same packets. Voice needs 64-bit Windows.

This is **not a VPN**. Your IP isn't hidden, and traffic isn't encrypted beyond normal HTTPS. Sites and voice servers that are blocked by IP address, or traffic that is only throttled, won't be fixed by this.

## Usage (Windows 7/8/10/11)

1. Extract the folder somewhere permanent, for example `C:\ValSync-Nile`.
2. Right-click `valsync_nile.cmd` → **Run as administrator**.
3. **Websites:** choose `1` and open a blocked site in a new tab. If it doesn't load, close the ValSync Nile window and try 2 → 3 → 4 → 5.
4. **Voice:** from the same menu, choose `V1`, then fully restart the game or Discord. If voice still fails, close the "ValSync Nile Voice" window and try `V2`.
5. Once both work, run `service_install.cmd` as administrator. It asks for a website profile and a voice profile, then installs them as services that start with Windows.

| File | What it does |
|---|---|
| `valsync_nile.cmd` | Menu: try website and voice profiles, install or remove services |
| `quick_start.cmd` | One-click start with website profile 1 |
| `service_install.cmd` | Installs the `ValSync` and (optional) `ValSync-Voice` services |
| `service_remove.cmd` | Removes both services and the WinDivert drivers |
| `profiles.cmd` | Website profile flags. Edit this file to tune a profile. |
| `voice_profiles.cmd` | Voice profile flags |
| `egypt-blacklist.txt` | Domains used by website profile 6. Add one domain per line. |
| `voice\` | zapret `winws.exe` and its WinDivert 2.2 driver |

### Website profiles

| # | Flags | When to use |
|---|---|---|
| 1 | `-9` + DNS | Default, works on most ISPs |
| 2 | `-5 --set-ttl 5` + DNS | The Turkey default; try it if 1 fails |
| 3 | `-9 --frag-by-sni --fake-gen 5 --fake-resend 2` + DNS | Stricter DPI |
| 4 | `-5 --set-ttl 3 -q`, no DNS redirect | You already use DoH or a custom DNS |
| 5 | `-1 -q` + DNS | Old routers or VMs where fake packets break things |
| 6 | `-9 --frag-by-sni` + DNS + `--blacklist` | Only touches listed sites; other traffic is left alone |

### Voice profiles

| # | What it covers | Cost |
|---|---|---|
| V1 | Discord voice and STUN-based calls (WhatsApp, Telegram, many game voice SDKs) | Very light: the driver only picks up matching packets |
| V2 | Everything in V1, plus Vivox (Valorant, League of Legends, many others), any UDP voice on ports 12000-65535, and WireGuard VPN handshakes | Fake packets go out only at the start of each flow. Valorant's game traffic (ports 7000-8000) isn't touched. |

If V2 adds lag or breaks another game, go back to V1.

### Troubleshooting

- **Websites still blocked:** run `ipconfig /flushdns`, restart the browser, and turn off QUIC at `chrome://flags/#enable-quic` (in Firefox: `about:config` → `network.http.http3.enable`).
- **Voice still silent:** restart the game or Discord *after* starting voice bypass, because existing voice sessions don't reconnect by themselves. In Discord, try a different voice region. If neither V1 nor V2 helps, your ISP is probably blocking the voice server's IP, and only a VPN will fix that.
- **Antivirus deletes `WinDivert64.sys`, `WinDivert.dll` or `winws.exe`:** these are false positives on the packet driver. Add the folder as an exclusion.
- **Some sites break:** use website profile 6 so only the listed domains get the bypass.
- **Uninstall:** run `service_remove.cmd` as administrator, then delete the folder.

---

## بالعربي

أداة جاهزة لتخطي الحجب على شبكات WE وفودافون وأورنج واتصالات في مصر. بتفتح المواقع المحجوبة، وبتشغّل الفويس شات في الألعاب (فالورانت، ديسكورد، مكالمات واتساب وتليجرام).

1. فك الضغط في مكان ثابت، مثلاً `C:\ValSync-Nile`.
2. كليك يمين على `valsync_nile.cmd` ← **تشغيل كمسؤول (Run as administrator)**.
3. **المواقع:** اختار `1` وجرّب الموقع المحجوب. لو مافتحش، اقفل نافذة ValSync Nile وجرّب 2 ثم 3 ثم 4 ثم 5.
4. **الفويس:** اختار `V1`، وبعدين اقفل اللعبة أو ديسكورد وافتحهم تاني. لو الصوت لسه مش شغال، اقفل نافذة "ValSync Nile Voice" وجرّب `V2`.
5. لما الاتنين يشتغلوا، شغّل `service_install.cmd` كمسؤول واختار البروفايلات، عشان يشتغلوا تلقائياً مع الويندوز.
6. لإزالته: شغّل `service_remove.cmd` كمسؤول.

الأداة دي **مش VPN**. مش بتخفي الـ IP بتاعك، والمواقع أو سيرفرات الصوت المحجوبة بالـ IP مش هتشتغل بيها.

## Credits & license

- GoodbyeDPI by [ValdikSS](https://github.com/ValdikSS/GoodbyeDPI), Apache-2.0. Binaries are unmodified from the official 0.2.2 release.
- zapret by [bol-van](https://github.com/bol-van/zapret), MIT. `winws.exe`, `cygwin1.dll` (LGPLv3) and WinDivert are unmodified from the official v72.13 release. Their SHA-256 hashes match the release's `sha256sum.txt`, and `WinDivert64.sys` matches the official WinDivert 2.2.2 release.
- WinDivert by basil00, LGPLv3. See `licenses/` and `voice/licenses/`.
- The preset approach is inspired by [cagritaskn/GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey).
