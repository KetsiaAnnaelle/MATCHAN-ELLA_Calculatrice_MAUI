using System.Globalization;

namespace Calculatrice;

public partial class MainPage : ContentPage
{
    const int MaxDigits = 15;
    const double MaxDecimal = 7.9e28;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Palette de l'afficheur
    static readonly Color Phosphor = Color.FromArgb("#D4F8E3");
    static readonly Color Soft     = Color.FromArgb("#6E9A8B");
    static readonly Color Lit      = Color.FromArgb("#7BE3B0");
    static readonly Color Dim      = Color.FromArgb("#2A3B38");
    static readonly Color Alert    = Color.FromArgb("#FF6B5A");
    static readonly Color Graphite = Color.FromArgb("#2B2F31");

    decimal? _left;           // premier opérande
    string? _op;              // opérateur en attente
    string _current = "0";    // saisie / résultat affiché
    string _expr = "";        // opération affichée au-dessus
    bool _startNew = true;    // la prochaine saisie remplace l'affichage
    bool _justEvaluated;      // vient d'appuyer sur "=" ou une fonction
    bool _operandReady;       // 2e opérande fourni par une fonction (ex. 5 + √9)
    bool _error;              // état d'erreur

    bool _degrees = true;     // mode angulaire : Deg (true) ou Rad (false)
    bool _sciOn;              // panneau scientifique affiché en portrait
    bool _portrait = true;
    bool _compactKeyboardMode;

    public MainPage()
    {
        InitializeComponent();
        BuildGauge();
        ApplyKeysLayout();
        Refresh();
    }

    // Jauge de saisie : une graduation par chiffre possible
    void BuildGauge()
    {
        for (int i = 0; i < MaxDigits; i++)
        {
            TickRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            TickRow.Add(new BoxView
            {
                WidthRequest = 3,
                HeightRequest = (i % 5 == 4) ? 12 : 7,
                CornerRadius = 1.5,
                Color = Dim,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End
            }, i, 0);
        }
    }

    // ---------- Disposition adaptative ----------

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0 || KeysArea == null) return;

        _portrait = height > width;
        _compactKeyboardMode = _portrait
            ? height < 700 || width < 360
            : width < 760;
        TitleBar.IsVisible = _portrait || _compactKeyboardMode;
        AppTitle.IsVisible = !_compactKeyboardMode;
        ApplyKeysLayout();
    }

    // Sur les écrans compacts, chaque clavier utilise toute la zone disponible.
    // Ailleurs, les panneaux restent affichés simultanément.
    void ApplyKeysLayout()
    {
        var rows = KeysArea.RowDefinitions;
        var cols = KeysArea.ColumnDefinitions;

        if (_compactKeyboardMode)
        {
            SciGrid.IsVisible = _sciOn;
            KeypadGrid.IsVisible = !_sciOn;
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[1].Height = new GridLength(0);
            cols[0].Width = new GridLength(1, GridUnitType.Star);
            cols[1].Width = new GridLength(0);
            Place(SciGrid, 0, 0, 2);
            Place(KeypadGrid, 0, 0, 2);
        }
        else if (_portrait)
        {
            SciGrid.IsVisible = _sciOn;
            KeypadGrid.IsVisible = true;
            rows[0].Height = _sciOn ? new GridLength(4, GridUnitType.Star) : new GridLength(0);
            rows[1].Height = new GridLength(5, GridUnitType.Star);
            cols[0].Width = new GridLength(1, GridUnitType.Star);
            cols[1].Width = new GridLength(0);
            Place(SciGrid, 0, 0, 2);
            Place(KeypadGrid, 1, 0, 2);
        }
        else
        {
            SciGrid.IsVisible = true;
            KeypadGrid.IsVisible = true;
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[1].Height = new GridLength(0);
            cols[0].Width = new GridLength(1, GridUnitType.Star);
            cols[1].Width = new GridLength(1, GridUnitType.Star);
            Place(SciGrid, 0, 0, 1);
            Place(KeypadGrid, 0, 1, 1);
        }

        Resources["KeyFontSize"] = _portrait ? (_compactKeyboardMode ? 25d : (_sciOn ? 22d : 26d)) : 17d;
        Resources["SciFontSize"] = _portrait ? (_compactKeyboardMode ? 17d : 15d) : 14d;
        SciToggle.Text = _compactKeyboardMode && _sciOn ? "Standard" : "Scientifique";
        SciToggle.BackgroundColor = _sciOn ? Graphite : Colors.Transparent;
        SciToggle.TextColor = _sciOn ? Colors.White : Graphite;
    }

    static void Place(View v, int row, int col, int colSpan)
    {
        Grid.SetRow(v, row);
        Grid.SetColumn(v, col);
        Grid.SetColumnSpan(v, colSpan);
    }

    void OnSciToggle(object sender, EventArgs e)
    {
        _sciOn = !_sciOn;
        ApplyKeysLayout();
    }

    void OnAngleMode(object sender, EventArgs e)
    {
        _degrees = !_degrees;
        AngleButton.Text = _degrees ? "Deg" : "Rad";
    }

    // ---------- Gestionnaires d'événements ----------

    void OnDigit(object sender, EventArgs e)
    {
        Haptic();
        if (_error) Reset();
        var d = ((Button)sender).Text;

        if (_startNew)
        {
            if (_justEvaluated) { _left = null; _op = null; _expr = ""; _justEvaluated = false; }
            _current = d;
            _startNew = false;
            _operandReady = false;
        }
        else if (_current == "0") _current = d;
        else if (_current == "-0") _current = "-" + d;
        else if (_current.Count(char.IsDigit) < MaxDigits) _current += d;

        Refresh();
    }

    void OnDecimal(object sender, EventArgs e)
    {
        Haptic();
        if (_error) Reset();

        if (_startNew)
        {
            if (_justEvaluated) { _left = null; _op = null; _expr = ""; _justEvaluated = false; }
            _current = "0.";
            _startNew = false;
            _operandReady = false;
        }
        else if (!_current.Contains('.')) _current += ".";

        Refresh();
    }

    // Sert aux opérateurs + − × ÷ et à la puissance x^y
    void OnOperator(object sender, EventArgs e)
    {
        if (_error) return;
        Haptic();
        var text = ((Button)sender).Text;
        var newOp = text == "x^y" ? "^" : text;
        var value = Parse(_current);
        bool hasSecond = !_startNew || _operandReady;

        if (_op != null && hasSecond)
        {
            // Enchaînement : 2 + 3 × ... => on calcule d'abord 2 + 3
            if (!TryCompute(_left!.Value, value, _op, out var r)) return;
            _left = r;
            _current = Format(r);
        }
        else if (_op == null)
        {
            _left = value;
        }
        // sinon : simple changement d'opérateur

        _op = newOp;
        _expr = "";
        _startNew = true;
        _justEvaluated = false;
        _operandReady = false;
        Refresh();
    }

    void OnEquals(object sender, EventArgs e)
    {
        if (_error || _op == null || _left == null) return;
        Haptic();

        var b = Parse(_current);
        var a = _left.Value;
        var op = _op;

        if (!TryCompute(a, b, op, out var r)) return;

        _expr = $"{Format(a)} {op} {Format(b)} =";
        _current = Format(r);
        _left = null;
        _op = null;
        _startNew = true;
        _justEvaluated = true;
        _operandReady = false;
        Refresh();
        Flash();
    }

    // Fonctions scientifiques à un seul argument (√, x², sin, ln, π, ...)
    void OnScientific(object sender, EventArgs e)
    {
        if (_error) Reset();
        Haptic();
        var key = ((Button)sender).Text;

        decimal r;
        string label;
        bool isConst = key is "π" or "e";

        if (isConst)
        {
            r = key == "π" ? (decimal)Math.PI : (decimal)Math.E;
            label = key;
        }
        else if (!TryUnary(key, Parse(_current), out r, out label, out var err))
        {
            SetError(label, err!);
            return;
        }

        _current = Format(r);
        _startNew = true;

        if (_op == null)
        {
            _expr = isConst ? label : label + " =";
            _left = null;
            _justEvaluated = true;
        }
        else
        {
            _operandReady = true; // ex. 5 + √9 : le 2e opérande est prêt
        }

        Refresh();
        Flash();
    }

    void OnClear(object sender, EventArgs e)
    {
        Reset();
        Refresh();
    }

    void OnBackspace(object sender, EventArgs e)
    {
        if (_error) { Reset(); Refresh(); return; }
        if (_startNew) return; // on n'efface pas un résultat

        _current = _current[..^1];
        if (_current is "" or "-") _current = "0";
        Refresh();
    }

    void OnSign(object sender, EventArgs e)
    {
        if (_error || (Parse(_current) == 0 && !_current.Contains('.'))) return;
        _current = _current.StartsWith('-') ? _current[1..] : "-" + _current;
        Refresh();
    }

    void OnPercent(object sender, EventArgs e)
    {
        if (_error) return;
        var v = Parse(_current);

        // 200 + 10 % => 10 % de 200 = 20 ; sinon simple division par 100
        if (_left != null && (_op is "+" or "−"))
            v = _left.Value * v / 100m;
        else
            v /= 100m;

        _current = Format(v);
        _startNew = false;
        Refresh();
    }

    // Garde le texte aligné à droite quand il est plus court que la zone visible
    void OnScrollSizeChanged(object sender, EventArgs e) => Refresh();

    // ---------- Logique ----------

    bool TryCompute(decimal a, decimal b, string op, out decimal result)
    {
        result = 0;
        try
        {
            switch (op)
            {
                case "+": result = a + b; break;
                case "−": result = a - b; break;
                case "×": result = a * b; break;
                case "÷":
                    if (b == 0)
                    {
                        SetError($"{Format(a)} ÷ 0", "Division par zéro impossible");
                        return false;
                    }
                    result = a / b;
                    break;
                case "^":
                {
                    string expr = $"{Format(a)} ^ {Format(b)}";
                    if (a == 0 && b < 0)
                    {
                        SetError(expr, "Division par zéro impossible");
                        return false;
                    }
                    double p = Math.Pow((double)a, (double)b);
                    if (double.IsNaN(p))
                    {
                        SetError(expr, "Entrée invalide");   // ex. (-8)^0.5
                        return false;
                    }
                    if (!ToDecimal(p, out result))
                    {
                        SetError(expr, "Résultat trop grand");
                        return false;
                    }
                    break;
                }
            }
            return true;
        }
        catch (OverflowException)
        {
            SetError($"{Format(a)} {op} {Format(b)}", "Résultat trop grand");
            return false;
        }
    }

    // Calcule une fonction à un argument. label = texte affiché au-dessus du résultat.
    bool TryUnary(string key, decimal x, out decimal r, out string label, out string? err)
    {
        r = 0;
        err = null;
        double d = (double)x;
        string s = Format(x);
        double rad = _degrees ? d * Math.PI / 180.0 : d;
        string unit = _degrees ? "°" : " rad";
        double v;

        switch (key)
        {
            case "√":
                label = $"√({s})";
                if (x < 0) { err = "Entrée invalide"; return false; }
                v = Math.Sqrt(d);
                break;

            case "x²":
                label = $"({s})²";
                try { r = x * x; return true; }
                catch (OverflowException) { err = "Résultat trop grand"; return false; }

            case "1/x":
                label = $"1/({s})";
                if (x == 0) { err = "Division par zéro impossible"; return false; }
                r = 1m / x;
                return true;

            case "sin":
                label = $"sin({s}{unit})";
                v = Math.Sin(rad);
                break;

            case "cos":
                label = $"cos({s}{unit})";
                v = Math.Cos(rad);
                break;

            case "tan":
                label = $"tan({s}{unit})";
                if (Math.Abs(Math.Cos(rad)) < 1e-12) { err = "Tangente non définie"; return false; }
                v = Math.Tan(rad);
                break;

            case "n!":
            {
                label = $"{s}!";
                if (x < 0 || x != decimal.Truncate(x)) { err = "Entrée invalide"; return false; }
                if (x > 27) { err = "Résultat trop grand"; return false; }
                decimal f = 1;
                for (int i = 2; i <= (int)x; i++) f *= i;
                r = f;
                return true;
            }

            case "ln":
                label = $"ln({s})";
                if (x <= 0) { err = "Entrée invalide"; return false; }
                v = Math.Log(d);
                break;

            case "log":
                label = $"log({s})";
                if (x <= 0) { err = "Entrée invalide"; return false; }
                v = Math.Log10(d);
                break;

            case "|x|":
                label = $"|{s}|";
                r = Math.Abs(x);
                return true;

            case "e^x":
                label = $"e^({s})";
                v = Math.Exp(d);
                break;

            case "10^x":
                label = $"10^({s})";
                v = Math.Pow(10, d);
                break;

            default:
                label = key;
                err = "Fonction inconnue";
                return false;
        }

        if (!ToDecimal(v, out r)) { err = "Résultat trop grand"; return false; }
        return true;
    }

    static bool ToDecimal(double d, out decimal r)
    {
        r = 0;
        if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > MaxDecimal) return false;
        r = (decimal)d;
        return true;
    }

    void SetError(string expr, string message)
    {
        _error = true;
        _expr = expr;
        _current = message;
        _left = null;
        _op = null;
        _startNew = true;
        _operandReady = false;
        Refresh();
    }

    void Reset()
    {
        _left = null; _op = null; _current = "0"; _expr = "";
        _startNew = true; _justEvaluated = false; _error = false; _operandReady = false;
    }

    static decimal Parse(string s) => decimal.Parse(s, NumberStyles.Float, Inv);

    static string Format(decimal v)
    {
        var r = Math.Round(v, 10);
        if (r == 0) r = 0m; // évite "-0"
        return r.ToString("0.##########", Inv);
    }

    static void Haptic()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* non supporté sur cette plateforme */ }
    }

    // Le résultat "s'allume" : mouvement qui répond à l'action
    void Flash()
    {
        ResultLabel.Opacity = 0.3;
        _ = ResultLabel.FadeTo(1, 180, Easing.CubicOut);
    }

    void Refresh()
    {
        ExpressionLabel.Text = _error ? _expr
                             : _op != null ? $"{Format(_left ?? 0)} {_op}"
                             : _expr;

        ResultLabel.Text = _current;
        ResultLabel.TextColor = _error ? Alert : Phosphor;

        // Réduit la police pour les longs nombres / messages
        ResultLabel.FontSize = _error ? 22
                             : _current.Length > 13 ? 32
                             : _current.Length > 9 ? 44
                             : 56;

        // Voyant et libellé d'état
        StateDot.Color = _error ? Alert : Lit;
        StatusLabel.TextColor = _error ? Alert : Soft;
        StatusLabel.Text = _error ? "Erreur"
                         : _justEvaluated ? "Résultat"
                         : (_current == "0" && _op == null && _expr == "") ? "Prêt"
                         : "Saisie";

        // Jauge : nombre de chiffres utilisés
        int used = _error ? 0 : Math.Min(MaxDigits, _current.Count(char.IsDigit));
        int i = 0;
        foreach (var tick in TickRow.Children.OfType<BoxView>())
            tick.Color = i++ < used ? Lit : Dim;

        // Alignement à droite même dans un ScrollView horizontal
        ResultLabel.MinimumWidthRequest = Math.Max(0, ResultScroll.Width);
        ExpressionLabel.MinimumWidthRequest = Math.Max(0, ExprScroll.Width);

        // Toujours montrer la fin du nombre saisi
        _ = ResultScroll.ScrollToAsync(ResultLabel, ScrollToPosition.End, false);
    }
}
