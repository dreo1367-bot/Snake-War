namespace BallGame;

/// <summary>Главное окно игры: ловим мышкой мячи и смешно бросаем их!</summary>
public class MainForm : Form
{
    private readonly Random _rnd = new();
    private readonly List<Ball> _balls = new();
    private readonly List<Particle> _parts = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private Ball _held;
    private readonly List<PointF> _trail = new(); // последние позиции мыши для расчёта силы броска
    private float _lastDt = 1f / 60f;
    private double _throws, _bounces;
    private string _banner = "";
    private double _bannerUntil;

    public MainForm()
    {
        Text = "🏀 Весёлый Мяч 2D — бери и бросай!";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);
        Size = new Size(960, 640);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(30, 34, 60);
        Cursor = Cursors.Cross;

        SpawnBall();
        SpawnBall();
        SpawnBall();

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
        _timer.Start();

        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Space) AddRandomBall();
            if (e.KeyCode == Keys.R) ResetWorld();
            if (e.KeyCode == Keys.M) Sound.Enabled = !Sound.Enabled;
        };
    }

    // ---------- мир ----------

    private void SpawnBall()
    {
        float r = 30 + _rnd.Next(35);
        var b = new Ball(
            r + _rnd.Next(Width - 2 * (int)r),
            r + _rnd.Next(Math.Max(1, Height / 3)),
            r, Ball.RandColor(_rnd), "🙂");
        b.VX = (_rnd.Next(400) - 200);
        _balls.Add(b);
    }

    private void AddRandomBall()
    {
        if (_balls.Count >= 40) return;
        SpawnBall();
        ShowBanner("Новый мяч! 🎾");
    }

    private void ResetWorld()
    {
        _balls.Clear();
        _parts.Clear();
        _held = null;
        _throws = _bounces = 0;
        for (int i = 0; i < 3; i++) SpawnBall();
        ShowBanner("Мир сброшен 🔄");
    }

    private void ShowBanner(string text)
    {
        _banner = text;
        _bannerUntil = _clock.Elapsed.TotalSeconds + 1.5;
    }

    // ---------- ввод ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (e.Clicks >= 2) { AddRandomBall(); return; }

        for (int i = _balls.Count - 1; i >= 0; i--)
            if (_balls[i].Contains(e.X, e.Y))
            {
                _held = _balls[i];
                _balls.RemoveAt(i);
                _held.VX = _held.VY = _held.Spin = 0;
                _trail.Clear();
                _trail.Add(new PointF(e.X, e.Y));
                Capture = true;
                break;
            }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_held == null) return;
        _held.X = e.X; _held.Y = e.Y;
        _trail.Add(new PointF(e.X, e.Y));
        while (_trail.Count > 10) _trail.RemoveAt(0);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_held == null) return;
        Capture = false;

        // скорость = средний вектор движения мыши за последние ~10 кадров
        float vx = 0, vy = 0;
        if (_trail.Count >= 2)
        {
            var a = _trail[0];
            var b = _trail[^1];
            float dt = Math.Max(_trail.Count * _lastDt, 0.016f);
            vx = (b.X - a.X) / dt;
            vy = (b.Y - a.Y) / dt;
        }
        // ограничение максимальной силы броска
        float sp = MathF.Sqrt(vx * vx + vy * vy);
        const float maxSp = 2600f;
        if (sp > maxSp) { vx *= maxSp / sp; vy *= maxSp / sp; sp = maxSp; }

        _held.VX = vx;
        _held.VY = vy;
        _held.Spin = vx / _held.R * 3f;
        _balls.Add(_held);

        if (sp > 250)
        {
            _throws++;
            Sound.Throw(sp);
            _parts.AddRange(Particles.Spawn(_rnd, _held.X, _held.Y, _held.Color, 14, sp * 0.35f));
            if (sp > 1900) ShowBanner("МОЩНЫЙ БРОСОК! 💥");
            else if (sp > 1200) ShowBanner("Красивый бросок! 🔥");
        }
        _held = null;
        _trail.Clear();
    }

    // ---------- цикл ----------

    private void OnTick(object sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastT, 0.001, 0.05);
        _lastT = now;
        _lastDt = dt;

        int w = ClientSize.Width, h = ClientSize.Height;

        foreach (var b in _balls)
        {
            float oldVy = b.VY;
            b.Update(dt, w, h);
            // удар об пол?
            if (b.Y + b.R >= h - 1 && oldVy > 250)
            {
                _bounces++;
                Sound.Bounce(oldVy);
                _parts.AddRange(Particles.Spawn(_rnd, b.X, h - 2, b.Color, (int)(oldVy / 120), oldVy * 0.2f));
            }
        }

        // столкновения мяч-мяч (упругие)
        for (int i = 0; i < _balls.Count; i++)
            for (int j = i + 1; j < _balls.Count; j++)
                ResolvePair(_balls[i], _balls[j]);

        _parts.RemoveAll(p => { p.Update(dt); return p.Life <= 0; });

        Invalidate();
    }

    private double _lastT;

    private void ResolvePair(Ball a, Ball b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float distSq = dx * dx + dy * dy;
        float minDist = a.R + b.R;
        if (distSq <= 0.0001f || distSq >= minDist * minDist) return;

        float dist = MathF.Sqrt(distSq);
        float nx = dx / dist, ny = dy / dist;
        float overlap = minDist - dist;

        // расталкиваем
        a.X -= nx * overlap / 2; a.Y -= ny * overlap / 2;
        b.X += nx * overlap / 2; b.Y += ny * overlap / 2;

        // обмен скоростями вдоль нормали (масса ~ радиус^2)
        float ma = a.R * a.R, mb = b.R * b.R;
        float rvn = (b.VX - a.VX) * nx + (b.VY - a.VY) * ny;
        if (rvn > 0) return; // уже разлетаются
        float e = 0.9f;
        float imp = -(1 + e) * rvn / (1 / ma + 1 / mb);
        a.VX -= imp * nx / ma; a.VY -= imp * ny / ma;
        b.VX += imp * nx / mb; b.VY += imp * ny / mb;

        if (-rvn > 500)
        {
            Sound.Bounce(-rvn * 0.6f);
            _parts.AddRange(Particles.Spawn(_rnd, (a.X + b.X) / 2, (a.Y + b.Y) / 2, b.Color, 6, -rvn * 0.2f));
        }
    }

    // ---------- отрисовка ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        int w = ClientSize.Width, h = ClientSize.Height;

        // фон-градиент
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                   Color.FromArgb(34, 40, 76), Color.FromArgb(18, 20, 38), 90f))
            g.FillRectangle(bg, 0, 0, w, h);

        // пол
        using (var floor = new SolidBrush(Color.FromArgb(52, 60, 100)))
            g.FillRectangle(floor, 0, h - 6, w, 6);

        // частицы
        foreach (var p in _parts) p.Draw(g);

        // мячи
        foreach (var b in _balls) b.Draw(g);

        // перетаскиваемый мяч поверх всех + подсказка силы
        if (_held != null)
        {
            _held.Draw(g);
            if (_trail.Count >= 2)
            {
                using var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 3);
                g.DrawLine(pen, _trail[0], _trail[^1]);
            }
        }

        // HUD
        using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
        using (var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
        {
            string info = $"Бросков: {_throws}   Отскоков: {_bounces}   Мячей: {_balls.Count}";
            g.DrawString(info, font, shadow, 11, 11);
            g.DrawString(info, font, Brushes.White, 10, 10);

            string help = "ЛКМ — взять и бросать | Пробел — новый мяч | 2×клик — мяч под курсором | R — сброс | M — звук";
            g.DrawString(help, new Font("Segoe UI", 8.5f), shadow, 11, h - 27);
            g.DrawString(help, new Font("Segoe UI", 8.5f), Brushes.Silver, 10, h - 28);
        }

        // баннер
        if (_clock.Elapsed.TotalSeconds < _bannerUntil)
        {
            using var bf = new Font("Segoe UI Emoji", 22f, FontStyle.Bold);
            var sz = g.MeasureString(_banner, bf);
            g.DrawString(_banner, bf, Brushes.Gold, (w - sz.Width) / 2, 40);
        }
    }
}
