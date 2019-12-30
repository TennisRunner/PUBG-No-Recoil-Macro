using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;
using ExtensionMethods;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Speech.Synthesis;
 
namespace botshell
{
	using Settings = WFunctions.Settings;
	using Options = WFunctions.Options;
 
	public partial class fmMain : Form
	{
		const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
		const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
		const uint MOUSEEVENTF_LEFTUP = 0x0004;
		const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
		const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
		const uint MOUSEEVENTF_MOVE = 0x0001;
		const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
		const uint MOUSEEVENTF_RIGHTUP = 0x0010;
		const uint MOUSEEVENTF_XDOWN = 0x0080;
		const uint MOUSEEVENTF_XUP = 0x0100;
		const uint MOUSEEVENTF_WHEEL = 0x0800;
		const uint MOUSEEVENTF_HWHEEL = 0x01000;
 
		[DllImport("user32.dll")]
		static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData,
   UIntPtr dwExtraInfo);
 
		[DllImport("user32.dll")]
		static extern short GetAsyncKeyState(System.Windows.Forms.Keys vKey);
 
		const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
		const uint KEYEVENTF_KEYUP = 0x0002;
 
		[DllImport("user32.dll")]
		static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
 
		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();
 
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
 
		public class DirectBitmap : IDisposable
		{
			public Bitmap Bitmap { get; private set; }
			public Int32[] Bits { get; private set; }
			public bool Disposed { get; private set; }
			public int Height { get; private set; }
			public int Width { get; private set; }
 
			protected GCHandle BitsHandle { get; private set; }
 
			public DirectBitmap(string fileName)
			{
				Bitmap bmp = new Bitmap(fileName);
 
				Construct(bmp.Width, bmp.Height);
 
				using (Graphics g = Graphics.FromImage(this.Bitmap))
				{
					g.DrawImage(bmp, 0, 0);
				}
 
				bmp.Dispose();
			}
 
			private void Construct(int width, int height)
			{
				Width = width;
				Height = height;
				Bits = new Int32[width * height];
				BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
				Bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, BitsHandle.AddrOfPinnedObject());
 
			}
 
			public DirectBitmap(int width, int height)
			{
				Construct(width, height);
			}
 
			public void SetPixel(int x, int y, Color colour)
			{
				int index = x + (y * Width);
				int col = colour.ToArgb();
 
				Bits[index] = col;
			}
 
			public Color GetPixel(int x, int y)
			{
				int index = x + (y * Width);
				int col = Bits[index];
				Color result = Color.FromArgb(col);
 
				return result;
			}
 
			public void Dispose()
			{
				if (Disposed) return;
				Disposed = true;
				Bitmap.Dispose();
				BitsHandle.Free();
			}
		}
 
		public enum RunMode
		{
			STOP,
			RUN,
			PAUSE,
		}
		
		RunMode runMode;
 
		Settings settings;
		
		public int nudIndex = 0;
 
		public List<NumericUpDown> nudOptions;
 
 
		public class MaskPoint
		{
			public MaskPoint(int x, int y, float averagePixel)
			{
				this.pt = new Point(x, y);
				this.averagePixel = averagePixel;
			}
			public Point pt;
 
			public float averagePixel;
		}
 
		public class WeaponMask
		{
			public List<MaskPoint> points;
 
			public string filename;
		}
 
		public class ScopeMask
		{
			public List<MaskPoint> points;
			
			public string filename;
 
			public int strength;
		}
 
		public class ScopeEntry
		{
			public ScopeEntry()
			{
				this.strength = 1;
				this.fireDelay = 90;
				this.activationDelay = 100;
				this.compensationAmount = 1;
				this.compensationDelay = 1;
			}
 
			public int strength,
						fireDelay,
						activationDelay,
						compensationAmount,
						compensationDelay;
		}
 
		public class WeaponProfile
		{
			public WeaponProfile()
			{
				 scopeEntries = new List<ScopeEntry>();
			}
 
			public string name;
 
			public List<ScopeEntry> scopeEntries;
 
			public ScopeEntry currentScope;
 
			public void SetScope(int strength)
			{
				foreach (ScopeEntry s in scopeEntries)
				{
					if (s.strength == strength)
					{
						currentScope = s;
						break;
					}
				}
			}
		}
		
		public class PlayerWeapon
		{
			public PlayerWeapon()
			{
				scopeStrength = 1;
			}
 
			public string name;
 
			public int scopeStrength;
		}
 
 
		List<WeaponMask> weaponMasks;
 
		List<WeaponProfile> weaponProfiles;
 
 
		bool showStatus = false;
 
		bool skipWindowCheck = false;
 
 
 
 
		public fmMain()
		{
			InitializeComponent();
			CheckForIllegalCrossThreadCalls = false;
		}
 
		private void fmMain_Load(object sender, EventArgs e)
		{
			weaponProfiles = new List<WeaponProfile>();
 
			settings = new Settings("settings.txt");
			settings.isLoading = true;
 
			settings.GetValue("baby mode", cbBabyMode);
 
			if (File.Exists("profiles2.dat") == true)
			{
				weaponProfiles = Deserialize().ToList();
 
				setStatus(weaponProfiles.Count + " weapon profiles loaded");
			}
 
			nudOptions = new List<NumericUpDown>();
			nudOptions.Add(nudCompensationDelay);
			nudOptions.Add(nudCompensationAmount);
			nudOptions.Add(nudActivationDelay);
			nudOptions.Add(nudFireRateDelay);
 
 
		  
			settings.isLoading = false;
			
			bgwMain.RunWorkerAsync();
		}
 
		
 
 
 
		private void AccurateSleep(float seconds)
		{
			DateTime start = DateTime.Now;
 
			while ((DateTime.Now - start).TotalSeconds < seconds)
				Thread.Sleep(1);
		}
 
 
 
 
 
		private void setStatus(string s)
		{
			int lines;
 
			List<string> statuses;
 
			if (showStatus == true)
			{
				Debugger.Log(0, "cat", s + "\r\n");
 
				lines = tbStatus.Height / tbStatus.Font.Height;
				statuses = tbStatus.Text.Split('\n').ToList();
 
				if (statuses.Count > lines)
				{
					statuses.RemoveRange(lines, statuses.Count - lines);
 
					tbStatus.Text = string.Empty;
 
					for (int i = 0; i < statuses.Count; i++)
						tbStatus.Text += statuses[i] + "\n";
				}
 
				tbStatus.Text = (s + "\r\n" + tbStatus.Text).Trim('\n');
			}
		}
 
 
 
		IntPtr lastForegroundWindow;
 
		private bool IsGameActive()
		{
			IntPtr hWnd = GetForegroundWindow();
 
			if (hWnd != lastForegroundWindow)
			{
				StringBuilder b = new StringBuilder(255);
 
				GetWindowText(hWnd, b, 255);
 
				if (b.ToString().ToLower().IndexOf("battlegrounds") != -1)
				{
					return true;
				}
 
				lastForegroundWindow = hWnd;
			}
 
			return false;
		}
 
		private void DisplayCurrentWeapon(WeaponProfile p, ScopeEntry s)
		{
			settings.isLoading = true;
 
			setStatus("Updating current weapon display");
 
			if (p == null)
			{
				labActiveWeapon.Text = "None";
				setStatus("No weapon found");
			}
			else
			{
				labActiveWeapon.Text = "" + p.name;
				setStatus("Weapon is now: " + p.name);
			}
 
			if (s == null)
			{
				labActiveScope.Text = "None";
 
				nudCompensationAmount.Value = 0;
				nudCompensationDelay.Value = 0;
				nudFireRateDelay.Value = 0;
				nudActivationDelay.Value = 0;
			}
			else
			{
				labActiveScope.Text = "" + s.strength + "x";
				
				nudCompensationAmount.Value = s.compensationAmount;
				nudCompensationDelay.Value = s.compensationDelay;
				nudFireRateDelay.Value = s.fireDelay;
				nudActivationDelay.Value = s.activationDelay;
			}
 
			settings.isLoading = false;
 
		}
 
		private ScopeEntry GetWeaponScope(WeaponProfile weaponProfile, int strength)
		{
			if (weaponProfile == null)
				return null;
 
			return weaponProfile.scopeEntries.Where(x => x.strength == strength).FirstOrDefault();
		}
 
		public WeaponProfile GetWeaponByName(string name)
		{
			return weaponProfiles.Where(x => x.name == name).FirstOrDefault();
		}
 
 
		private bool IsScreenshotInTabMenu(DirectBitmap bmp)
		{
			bool result;
 
 
			result = false;
 
			List<Point> timePoints = new List<Point>(new Point[] { new Point(479, 116), new Point(470, 103), new Point(489, 109) });
 
			for (int i = 0; i < timePoints.Count; i++)
			{
				float bright = bmp.GetPixel(timePoints[i].X, timePoints[i].Y).GetBrightness();
				
				if (bmp.GetPixel(timePoints[i].X, timePoints[i].Y).GetBrightness() > 0.9f)
				{
					if (i == timePoints.Count - 1)
						result = true;
				}
				else
					break;
			}
 
			return result;
		}
 
		private bool IsScopeAvailabe(DirectBitmap bmp, Point ptStart)
		{
			bool result = true;
 
 
			for (int i = 1; i < 20; i++)
			{
				float borderBrightness = bmp.GetPixel(ptStart.X + i, ptStart.Y - 1).GetBrightness();
 
				float topBrightness = bmp.GetPixel(ptStart.X + i, ptStart.Y - 2).GetBrightness();
 
				if (borderBrightness > topBrightness)
				{
					float bottomBrightness = bmp.GetPixel(ptStart.X + i, ptStart.Y).GetBrightness();
 
					if (bottomBrightness > borderBrightness)
					{
						result = false;
						break;
					}
 
				}
				else
				{
					result = false;
					break;
				}
			}
 
 
			return result;
		}
 
		private WeaponProfile WeaponMaskToProfile(WeaponMask m)
		{
			return weaponProfiles.Where(x => x.name == m.filename).FirstOrDefault();
		}
 
 
		private bool DoesMaskMatch(DirectBitmap bmp, Point ptStart, List<MaskPoint> points)
		{
			bool result;
			int matchCount = 0;
 
			float matchPercent = 0.0f;
 
			result = false;
 
			for (int i = 0; i < points.Count; i++)
			{
				Color pixel = bmp.GetPixel(points[i].pt.X + ptStart.X, points[i].pt.Y + ptStart.Y);
 
				float average = ((pixel.R + pixel.G + pixel.B) / 3.0f);
 
				float difference = Math.Abs(average - points[i].averagePixel);
 
				if (Math.Abs(average - points[i].averagePixel) <= 5.0f)
				{
					matchCount++;
				}
			}
 
			matchPercent = matchCount / (float)points.Count;
 
			if (matchPercent > 0.4f)
			{
				result = true;
			}
 
 
			return result;
		}
 
 
		private void bgwMain_DoWork(object sender, DoWorkEventArgs e)
		{
			int startTime;
 
			int nextFireTime;
			
			PlayerWeapon[] playerWeapons = new PlayerWeapon[2] { new PlayerWeapon(), new PlayerWeapon() };
			
 
 
 
			startTime = 0;
			nextFireTime = 0;
 
			runMode = RunMode.RUN;
 
			bool doingNade = false;
 
			bool slept = false;
 
			int nextWindowTime = Environment.TickCount;
 
			IntPtr lastForegroundWindow = IntPtr.Zero;
 
			bool isGameActive = false;
 
			float brightnessMinimum;
 
 
			Point scopeStart1 = new Point(1586, 115);
			Point scopeStart2 = new Point(1586, 345);
 
			Point start = new Point(890, 920);
 
			Size area = new Size(140, 45);
 
			Size scopeArea = new Size(48, 43);
 
 
			List<ScopeMask> scopeMasks;
 
			int currentWeaponGameIndex = 1;
 
			SpeechSynthesizer speaker = new SpeechSynthesizer();
 
			speaker.Volume = 100;
 
			brightnessMinimum = 0.80f;
 
			float scopeBrightnessMaximum = 0.15f;
 
			bool firedGun = false;
 
 
			int lastWeaponSlot = 1,
				currentWeaponSlot = 1;
 
			List<string> singleFireWeapons = new List<string>(new string[] { "awm", "crossbow", "kar98k", "m24" });
 
			weaponMasks = new List<WeaponMask>();
			scopeMasks = new List<ScopeMask>();
 
			
			
 
			// Load the scope masks into objects
			foreach (string s in Directory.GetFiles("input images\\raw\\menu scopes"))
			{
				ScopeMask mask;
 
				Bitmap bmp;
 
 
				bmp = new Bitmap(s);
				
				mask = new ScopeMask();
				mask.filename = s.Split("\\").Last().Split(".").First();
				mask.points = new List<MaskPoint>();
 
				for (int y = 0; y < bmp.Height; y++)
				{
					for (int x = 0; x < bmp.Width; x += 10)
					{
						Color pixel = bmp.GetPixel(x, y);
 
						if ((pixel.R == Color.Magenta.R && pixel.G == Color.Magenta.G && pixel.B == Color.Magenta.B) == false)
						{
							mask.points.Add(new MaskPoint(x, y, ((pixel.R + pixel.G + pixel.B) / 3.0f)));
						}
					}
				}
 
				mask.strength = int.Parse(mask.filename.Split(".").First().Split("-").First().Replace("x", ""));
 
				setStatus("Point count: " + mask.points.Count);
 
				bmp.Dispose();
				scopeMasks.Add(mask);
			}
 
			scopeMasks = scopeMasks.OrderBy(x => x.points.Count).ToList();
 
			scopeMasks.Reverse();
 
 
 
			// Load the gun masks into objects
			foreach (string s in Directory.GetFiles("input images\\raw\\menu guns"))
			{
				WeaponMask mask;
 
				Bitmap bmp;
 
 
				bmp = new Bitmap(s);
 
 
				mask = new WeaponMask();
				mask.filename = s.Split("\\").Last().Split(".").First().ToLower();
				mask.points = new List<MaskPoint>();
 
				for (int y = 0; y < bmp.Height; y += 1)
				{
					for (int x = 0; x < bmp.Width; x++)
					{
						Color pixel = bmp.GetPixel(x, y);
 
						if ((pixel.R == Color.Magenta.R && pixel.G == Color.Magenta.G && pixel.B == Color.Magenta.B) == false)
						{
							mask.points.Add(new MaskPoint(x, y, ((pixel.R + pixel.G + pixel.B) / 3.0f)));
						}
					}
				}
 
				mask.points.Sort((x, y) =>
				{
					if (x.averagePixel == y.averagePixel)
						return 0;
					else if (x.averagePixel < y.averagePixel)
						return -1;
					else
						return 1;
				});
 
				mask.points.Reverse();
 
				List<MaskPoint> final = new List<MaskPoint>();
 
				for (int i = 0; i < mask.points.Count; i += mask.points.Count / 20)
				{
					final.Add(mask.points[i]);
				}
 
				mask.points = final;
 
				setStatus("Weapon Point count: " + mask.points.Count);
 
 
				bmp.Dispose();
				weaponMasks.Add(mask);
			}
			
			weaponMasks = weaponMasks.OrderBy(x => x.points.Count).ToList();
			
			weaponMasks.Reverse();
 
 
 
			// Create the weapon profiles
			foreach (WeaponMask a in weaponMasks)
			{
				// If the profile doesn't exist yet
				if (weaponProfiles.Where(x => x.name == a.filename).FirstOrDefault() == null)
				{
					WeaponProfile p = new WeaponProfile();
 
					p.name = a.filename;
 
					weaponProfiles.Add(p);
				}
			}
 
			// Add the scope profiles to each weapon
			foreach (WeaponProfile p in weaponProfiles)
			{
				foreach (ScopeMask s in scopeMasks)
				{
					// If it doesn't have an entry for that scope strength, then create one
					if (p.scopeEntries.Where(x => x.strength == s.strength).FirstOrDefault() == null)
					{
						ScopeEntry se = new ScopeEntry();
 
						se.strength = s.strength;
 
						p.scopeEntries.Add(se);
					}
				}
			}
 
 
 
			
			Point ptGunMaskStart1 = new Point(1445, 158);
			Point ptGunMaskStart2 = new Point(1445, 388);
 
			Point ptScopeMaskStart1 = new Point(1585, 114);
			Point ptScopeMaskStart2 = new Point(1585, 344);
				
			List<string> screenshots;
 
 
 
 
			   
				
			
 
			int lastScreenshotTime = 0;
 
			int nextCompensationTime = 0;
 
			bool requestingScreenshot = false;
			
 
			while (true)
			{
				try
				{
					if ((GetAsyncKeyState(Keys.Up) & 1) == 1)
					{
						nudIndex--;
 
						if (nudIndex < 0)
							nudIndex = 0;
 
						this.ActiveControl = null;
 
						
						foreach (NumericUpDown a in nudOptions)
							a.BackColor = SystemColors.Control;
 
						nudOptions[nudIndex].BackColor = Color.LightGray;
					}
					else if ((GetAsyncKeyState(Keys.Down) & 1) == 1)
					{
						nudIndex++;
 
						if (nudIndex > 3)
							nudIndex = 3;
 
						this.ActiveControl = null;
 
						foreach (NumericUpDown a in nudOptions)
							a.BackColor = SystemColors.Control;
 
						nudOptions[nudIndex].BackColor = Color.LightGray;
					}
					else if ((GetAsyncKeyState(Keys.Left) & 1) == 1)
					{
						if (nudOptions[nudIndex].Value > nudOptions[nudIndex].Minimum)
						{
							if (GetAsyncKeyState(Keys.LShiftKey) != 0)
							{
								nudOptions[nudIndex].Value = Math.Max(0, nudOptions[nudIndex].Value - 100);
							}
							else
								nudOptions[nudIndex].Value -= 1;
						}
					}
					else if ((GetAsyncKeyState(Keys.Right) & 1) == 1)
					{
						if (nudOptions[nudIndex].Value < nudOptions[nudIndex].Maximum)
						{
							if (GetAsyncKeyState(Keys.LShiftKey) != 0)
							{
								nudOptions[nudIndex].Value = Math.Min(nudOptions[nudIndex].Maximum, nudOptions[nudIndex].Value + 100);
							}
							else
								nudOptions[nudIndex].Value += 1;
						}
					}
					else if ((GetAsyncKeyState(Keys.D1) & 1) == 1)
					{
						currentWeaponSlot = 0;
 
						WeaponProfile p = weaponProfiles.Where(x => x.name == playerWeapons[currentWeaponSlot].name).FirstOrDefault();
 
						ScopeEntry s = null;
 
						if (p != null)
							s = p.scopeEntries.Where(x => x.strength == playerWeapons[currentWeaponSlot].scopeStrength).FirstOrDefault();
 
						DisplayCurrentWeapon(p, s);
 
					}
					else if ((GetAsyncKeyState(Keys.D2) & 1) == 1)
					{
						currentWeaponSlot = 1;
 
						WeaponProfile p = weaponProfiles.Where(x => x.name == playerWeapons[currentWeaponSlot].name).FirstOrDefault();
 
						ScopeEntry s = null;
 
						if (p != null)
							s = p.scopeEntries.Where(x => x.strength == playerWeapons[currentWeaponSlot].scopeStrength).FirstOrDefault();
 
						DisplayCurrentWeapon(p, s);
					}
					else if ((GetAsyncKeyState(Keys.D5) & 1) == 1 || (GetAsyncKeyState(Keys.G) & 1) == 1)
					{
						currentWeaponSlot = 4;
						
						DisplayCurrentWeapon(null, null);
					}
					else if ((GetAsyncKeyState(Keys.Tab) & 1) == 1)
					{
						AccurateSleep(0.3f);
						requestingScreenshot = true;
					}
 
 
					if (Environment.TickCount > nextWindowTime)
					{
						isGameActive = IsGameActive();
 
						nextWindowTime = Environment.TickCount + 2000;
					}
					
					if (isGameActive == true || skipWindowCheck == true)
					{
						if (requestingScreenshot == true)
						{
							if (Environment.TickCount - lastScreenshotTime > 1000)
							{
								setStatus("Taking screenshot");
								keybd_event((int)Keys.F12, 0x9e, 0, UIntPtr.Zero);
								Thread.Sleep(10);
								keybd_event((int)Keys.F12, 0x9e, KEYEVENTF_KEYUP, UIntPtr.Zero);
								Thread.Sleep(10);
 
								requestingScreenshot = false;
							}
						}
 
 
						// Process any screenshots in the folder
						screenshots = Directory.GetFiles("input images\\screenshots").ToList();
 
						if (screenshots.Count > 0)
						{
							DirectBitmap rawTest = null;
 
							Thread.Sleep(25);
 
							screenshots[0] = screenshots[screenshots.Count - 1];
 
							setStatus("Screenshot found");
							
							for (int i = 0; i < 100; i++)
							{
								try
								{
									rawTest = new DirectBitmap(screenshots[0]);
 
									break;
								}
								catch (Exception x)
								{
									x.ToString();
									Thread.Sleep(10);
								}
							}
 
							if (rawTest != null)
							{
								if (IsScreenshotInTabMenu(rawTest) == true)
								{
									// While the tab is open, keep screenshotting
 
									requestingScreenshot = true;
 
									WeaponProfile p;
 
									for (int i = 0; i < 2; i++)
									{
										p = null;
 
										setStatus("Checking slot: " + i);
										Point ptCurrentGunMaskStart,
												ptCurrentScopeMaskStart;
 
										if (i == 0)
										{
											ptCurrentGunMaskStart = ptGunMaskStart1;
											ptCurrentScopeMaskStart = ptScopeMaskStart1;
										}
										else
										{
											ptCurrentGunMaskStart = ptGunMaskStart2;
											ptCurrentScopeMaskStart = ptScopeMaskStart2;
										}
 
 
										//Image compare for a weapon match
										foreach (WeaponMask m in weaponMasks)
										{
											if (DoesMaskMatch(rawTest, ptCurrentGunMaskStart, m.points) == true)
											{
												p = WeaponMaskToProfile(m);
 
												int index = weaponMasks.IndexOf(m);
 
												weaponMasks.Insert(0, m);
												weaponMasks.RemoveAt(index + 1);
 
												break;
											}
 
											Thread.Sleep(1);
										}
 
										// if a weapon was matched
										if (p != null)
										{
 
											int scopeStrength = 1;
											bool updateDisplay = false;
 
											setStatus("Matched: " + p.name);
											
											if (playerWeapons[i].name != p.name)
											{
												//Process.Start(screenshots[0]);
												playerWeapons[i].name = p.name;
												
												string name = playerWeapons[i].name;
												if (name == "pp-19 bizon")
												{
													name = "P. P. Bizon";
												}
												else if (name == "sks")
												{
													name = "S. K. S.";
												}
												else if (name == "aug")
												{
													name = "A. U. G.";
												}
												else if (name == "awm")
												{
													name = "A. W. M.";
												}
												else if (name == "slr")
												{
													name = "S. L. R.";
												}
												else if (name == "ak47")
												{
													name = "A. K. 47";
												}
 
												speaker.SpeakAsync(name + " " + (i == 0 ? "first" : "second") + " slot");
												updateDisplay = true;
											}
 
											foreach (ScopeMask s in scopeMasks)
											{
												if (DoesMaskMatch(rawTest, ptCurrentScopeMaskStart, s.points) == true)
												{
													setStatus("Matched scope: " + s.strength);
 
													scopeStrength = s.strength;
 
 
													int index = scopeMasks.IndexOf(s);
 
													scopeMasks.Insert(0, s);
													scopeMasks.RemoveAt(index + 1);
 
													if (cbBabyMode.Checked == true)
													{
														if (scopeStrength > 2)
															scopeStrength = 2;
													}
 
													break;
												}
 
												Thread.Sleep(1);
											}
 
											if (playerWeapons[i].scopeStrength != scopeStrength)
											{
												playerWeapons[i].scopeStrength = scopeStrength;
												
												speaker.SpeakAsync(playerWeapons[i].scopeStrength + " X. " + (i == 0 ? "first" : "second") + " slot");
												updateDisplay = true;
											}
 
											if (updateDisplay == true)
											{
												WeaponProfile p2 = weaponProfiles.Where(x => x.name == playerWeapons[currentWeaponSlot].name).FirstOrDefault();
 
												ScopeEntry s2 = null;
 
												if (p2 != null)
													s2 = p2.scopeEntries.Where(x => x.strength == playerWeapons[currentWeaponSlot].scopeStrength).FirstOrDefault();
 
												DisplayCurrentWeapon(p2, s2);
											}
										}
									}
								}
							}
 
							if (rawTest != null)
								rawTest.Dispose();
							
							setStatus("Deleting screenshots");
 
							foreach (string s in screenshots)
							{                                
								try
								{
									File.Delete(s);
								}
								catch (Exception x)
								{
									x.ToString();
								}
							}
 
							setStatus("Finished deleting");
						}
 
 
 
 
 
						if ((GetAsyncKeyState(Keys.LButton) != 0) && GetAsyncKeyState(Keys.RButton) == 0 && doingNade == false)
						{
							keybd_event((int)'L', 0x9e, 0, UIntPtr.Zero);
							doingNade = true;
						}
 
						// if its attempt to do a nade
						if (doingNade == true)
						{
							if ((GetAsyncKeyState(Keys.LButton) == 0) && GetAsyncKeyState(Keys.RButton) == 0)
							{
								keybd_event((int)'L', 0x9e, KEYEVENTF_KEYUP, UIntPtr.Zero);
								doingNade = false;
							}
						}
 
						// If its attempting to spray or is simulating a spray
						if (GetAsyncKeyState(Keys.LButton) != 0 && GetAsyncKeyState(Keys.RButton) != 0 && currentWeaponSlot < 2)
						{
							if (startTime == 0)
							{
								startTime = Environment.TickCount;
							}
 
							//If the fire button has been held long enough to activate compensation
							if (Environment.TickCount - startTime > nudActivationDelay.Value)
							{
								WeaponProfile currentWeaponProfile;
 
 
								currentWeaponProfile = weaponProfiles.Where(x => x.name == playerWeapons[currentWeaponSlot].name).FirstOrDefault();
								
								// If there is a weapon selected
								if (currentWeaponProfile != null)
								{
									ScopeEntry currentScopeEntry;
 
 
									currentScopeEntry = currentWeaponProfile.scopeEntries.Where(x => x.strength == playerWeapons[currentWeaponSlot].scopeStrength).FirstOrDefault();
									
									// If there is a scope available
									if (currentScopeEntry != null)
									{
										if (Environment.TickCount >= nextCompensationTime)
										{
											mouse_event(MOUSEEVENTF_MOVE, 0, currentScopeEntry.compensationAmount, 0, UIntPtr.Zero);
 
											nextCompensationTime = Environment.TickCount + currentScopeEntry.compensationDelay;
										}
 
 
										if (Environment.TickCount >= nextFireTime)
										{
											keybd_event((int)'L', 0x9e, 0, UIntPtr.Zero);
											nextFireTime = Environment.TickCount + (int)currentScopeEntry.fireDelay;
											Thread.Sleep(25);
										}
										else
										{
											// Do not try to auto fire on a gun that cannot be autofired
											if(singleFireWeapons.IndexOf(currentWeaponProfile.name) == -1)
												keybd_event((int)'L', 0x9e, KEYEVENTF_KEYUP, UIntPtr.Zero);
										}
									}
								}
							}
						}
						else
						{
							// if it didn't shoot
							if (startTime != 0 && nextFireTime == 0)
							{
								keybd_event((int)'L', 0x9e, 0, UIntPtr.Zero);
								Thread.Sleep(25);
								keybd_event((int)'L', 0x9e, KEYEVENTF_KEYUP, UIntPtr.Zero);
							}
							// If it did shoot, reset the fire key just in case
							else if(nextFireTime != 0)
							{
								keybd_event((int)'L', 0x9e, KEYEVENTF_KEYUP, UIntPtr.Zero);
							}
 
							startTime = 0;
							nextFireTime = 0;
						}
					}
				}
				catch (Exception x)
				{
					MessageBox.Show(x.ToString());
				}
 
				Thread.Sleep(1);
			}
		}
 
		
		
		private void fmMain_FormClosing(object sender, FormClosingEventArgs e)
		{
			Environment.Exit(0);
		}
		
		
		private void settingsChanged(object sender, EventArgs e)
		{
			//if (lbProfiles.SelectedIndex != -1 && settings.isLoading == false)
			//{
			//    //WeaponProfile p = profiles[lbProfiles.SelectedIndex];
 
 
			//    //p.compensations[0] = (int)nud1xCompensation.Value;
			//    //p.compensations[1] = (int)nud2xCompensation.Value;
			//    //p.compensations[2] = (int)nud3xCompensation.Value;
			//    //p.compensations[3] = (int)nud4xCompensation.Value;
			//    //p.compensations[4] = (int)nud6xCompensation.Value;
			//    //p.compensations[5] = (int)nud8xCompensation.Value;
			//    //p.autoShootDelay = (int)nudAutoShootDelay.Value;
 
			//    SaveProfiles();
			//}
 
			//settings.SetValue("autoshoot delay", nudAutoShootDelay);
			settings.SetValue("baby mode", cbBabyMode);
			settings.Save();
		}
		
		public static void Serialize(WeaponProfile[] input)
		{
			var serializer = new XmlSerializer(input.GetType());
			using (var sw = new StreamWriter(@"profiles2.dat"))
			{
				serializer.Serialize(sw, input);
			}
		}
 
		public static WeaponProfile[] Deserialize()
		{
			object obj;
 
			using (var stream = new StreamReader(@"profiles2.dat"))
			{
				var ser = new XmlSerializer(typeof(WeaponProfile[]));
				
				obj = ser.Deserialize(stream);
				stream.Close();
			}
		   
			return (WeaponProfile[])obj;
		}
 
		private void SaveProfiles()
		{
			Serialize(weaponProfiles.ToArray());
		}
 
		private void nudFireRateDelay_ValueChanged(object sender, EventArgs e)
		{
			if (settings.isLoading == false)
			{
				string weaponName = labActiveWeapon.Text.Replace("Active Weapon: ", "");
				string scopeStrength = labActiveScope.Text.Replace("Active Scope: ", "").Replace("x", "");
 
				foreach (WeaponProfile p in weaponProfiles)
				{
					if (p.name == weaponName)
					{
						foreach (ScopeEntry s in p.scopeEntries)
						{
							if (s.strength.ToString() == scopeStrength)
							{
								s.activationDelay = (int)nudActivationDelay.Value;
								s.compensationAmount = (int)nudCompensationAmount.Value;
								s.compensationDelay = (int)nudCompensationDelay.Value;
								s.fireDelay = (int)nudFireRateDelay.Value;
 
								SaveProfiles();
								break;
							}
						}
 
						break;
					}
				}
			}
		}
 
	}
}

