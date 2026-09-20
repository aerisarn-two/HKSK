namespace HKSK.Engine;

/// <summary>What an expression does when it runs.</summary>
/// <param name="Target">The variable an assignment writes, or null.</param>
/// <param name="Event">The event an <c>E if C</c> form sends, or null.</param>
public readonly record struct ExpressionEffect(string? Target, string? Event);

/// <summary>
/// One of the expressions a <c>hkbEvaluateExpressionModifier</c> evaluates.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is taken from the 261 distinct expressions the 49 projects
/// actually contain, not from Havok's -- see <c>NodeCensusTests.ExpressionCensus</c>.
/// Two forms appear: <c>variable = expression</c> (172) and
/// <c>Event if condition</c> (89, and the condition's parentheses are optional).
/// Seven functions are used -- <c>cond</c>, <c>fabs</c>, <c>clamp</c>, <c>max</c>,
/// <c>min</c>, <c>sind</c>, <c>cos</c> -- and the operators are
/// <c>|| &amp;&amp; ! == != &lt; &gt; &lt;= &gt;= + - * / %</c>.
/// </para>
/// <para>
/// <c>cond</c> is a Bethesda extension: Havok 6.6's own compiler emits zero tokens
/// for it, so it cannot be checked against the runtime and is implemented from what
/// the graphs need it to mean -- a three-argument select.
/// </para>
/// </remarks>
public sealed class Expression
{
    private readonly Node _root;

    private Expression(Node root, ExpressionEffect effect, string text)
    {
        _root = root;
        Effect = effect;
        Text = text;
    }

    /// <summary>What running it does.</summary>
    public ExpressionEffect Effect { get; }

    /// <summary>The text it was parsed from.</summary>
    public string Text { get; }

    /// <summary>Parses one, or null when the text is not a form we know.</summary>
    public static Expression? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            return new Parser(text).ParseStatement();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Evaluates it. For an assignment the value is written to the target and
    /// returned; for an event form the condition is returned, non-zero when it
    /// fires. False when a name it reads is not declared.
    /// </summary>
    public bool TryEvaluate(Variables variables, out float value)
    {
        value = 0f;
        if (!_root.TryEvaluate(variables, out value)) return false;

        if (Effect.Target is { } target && !variables.Set(target, value)) return false;

        return true;
    }

    public override string ToString() => Text;

    // ---- syntax tree -------------------------------------------------------

    private abstract class Node
    {
        public abstract bool TryEvaluate(Variables variables, out float value);
    }

    private sealed class Literal(float value) : Node
    {
        public override bool TryEvaluate(Variables variables, out float result)
        {
            result = value;
            return true;
        }
    }

    private sealed class Read(string name) : Node
    {
        public override bool TryEvaluate(Variables variables, out float result)
        {
            int at = variables.IndexOf(name);
            result = at < 0 ? 0f : variables.AsReal(at);
            return at >= 0;
        }
    }

    private sealed class Unary(string op, Node operand) : Node
    {
        public override bool TryEvaluate(Variables variables, out float result)
        {
            result = 0f;
            if (!operand.TryEvaluate(variables, out float x)) return false;

            result = op switch { "!" => x == 0f ? 1f : 0f, "-" => -x, _ => x };
            return true;
        }
    }

    private sealed class Binary(string op, Node left, Node right) : Node
    {
        public override bool TryEvaluate(Variables variables, out float result)
        {
            result = 0f;
            if (!left.TryEvaluate(variables, out float a)) return false;
            if (!right.TryEvaluate(variables, out float b)) return false;

            result = op switch
            {
                "||" => a != 0f || b != 0f ? 1f : 0f,
                "&&" => a != 0f && b != 0f ? 1f : 0f,
                "==" => a == b ? 1f : 0f,
                "!=" => a != b ? 1f : 0f,
                "<" => a < b ? 1f : 0f,
                ">" => a > b ? 1f : 0f,
                "<=" => a <= b ? 1f : 0f,
                ">=" => a >= b ? 1f : 0f,
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0f ? 0f : a / b,
                "%" => b == 0f ? 0f : a % b,
                _ => 0f,
            };

            return true;
        }
    }

    private sealed class Call(string name, List<Node> arguments) : Node
    {
        public override bool TryEvaluate(Variables variables, out float result)
        {
            result = 0f;

            // cond picks a branch, so only the branch taken has to resolve: a graph
            // may name a constant that the other creature's file declares.
            if (name == "cond" && arguments.Count == 3)
            {
                if (!arguments[0].TryEvaluate(variables, out float test)) return false;
                return arguments[test != 0f ? 1 : 2].TryEvaluate(variables, out result);
            }

            float[] values = new float[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
                if (!arguments[i].TryEvaluate(variables, out values[i])) return false;

            switch (name)
            {
                case "fabs" when values.Length == 1: result = Math.Abs(values[0]); return true;
                case "clamp" when values.Length == 3:
                    result = Math.Clamp(values[0], values[1], values[2]); return true;
                case "max" when values.Length == 2: result = Math.Max(values[0], values[1]); return true;
                case "min" when values.Length == 2: result = Math.Min(values[0], values[1]); return true;
                case "sind" when values.Length == 1:
                    result = (float)Math.Sin(values[0] * Math.PI / 180.0); return true;
                case "cos" when values.Length == 1:
                    result = (float)Math.Cos(values[0]); return true;
                default: return false;
            }
        }
    }

    // ---- parser ------------------------------------------------------------

    private sealed class Parser(string text)
    {
        private int _at;

        public Expression? ParseStatement()
        {
            int save = _at;
            string? name = ReadName();

            if (name is not null && Peek("==") is false && Take("="))
            {
                Node value = ParseOr();
                SkipSpace();
                return _at == text.Length
                    ? new Expression(value, new ExpressionEffect(name, null), text)
                    : null;
            }

            _at = save;
            name = ReadName(dotted: true);

            if (name is not null && TakeWord("if"))
            {
                Node condition = ParseOr();
                SkipSpace();
                return _at == text.Length
                    ? new Expression(condition, new ExpressionEffect(null, name), text)
                    : null;
            }

            // The third form: a bare condition, as hkbExpressionCondition carries it
            // on a transition -- "iRightHandType > 0", "(iLeftHandType != 5) && ...".
            // It writes nothing and sends nothing; its value is whether it holds.
            _at = save;
            Node bare = ParseOr();
            SkipSpace();
            return _at == text.Length
                ? new Expression(bare, new ExpressionEffect(null, null), text)
                : null;
        }

        private Node ParseOr() => ParseLeft(ParseAnd, "||");
        private Node ParseAnd() => ParseLeft(ParseEquality, "&&");
        private Node ParseEquality() => ParseLeft(ParseRelational, "==", "!=");
        private Node ParseRelational() => ParseLeft(ParseAdditive, "<=", ">=", "<", ">");
        private Node ParseAdditive() => ParseLeft(ParseMultiplicative, "+", "-");
        private Node ParseMultiplicative() => ParseLeft(ParseUnary, "*", "/", "%");

        private Node ParseLeft(Func<Node> next, params string[] operators)
        {
            Node left = next();

            while (true)
            {
                SkipSpace();
                string? op = operators.FirstOrDefault(Take);
                if (op is null) return left;

                left = new Binary(op, left, next());
            }
        }

        private Node ParseUnary()
        {
            SkipSpace();
            if (Take("!")) return new Unary("!", ParseUnary());
            if (Take("-")) return new Unary("-", ParseUnary());

            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            SkipSpace();

            if (Take("("))
            {
                Node inner = ParseOr();
                SkipSpace();
                if (!Take(")")) throw new FormatException();

                return inner;
            }

            // A name is tried first because one may begin with digits; ReadName
            // declines a run of digits that no letter follows, which is a number.
            if (ReadName() is not { } named)
            {
                if (_at >= text.Length || (!char.IsDigit(text[_at]) && text[_at] != '.'))
                    throw new FormatException();

                int start = _at;
                while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) _at++;

                return new Literal(float.Parse(text[start.._at],
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            string name = named;
            SkipSpace();

            if (!Take("(")) return new Read(name);

            List<Node> arguments = [];
            SkipSpace();

            if (!Take(")"))
            {
                do { arguments.Add(ParseOr()); SkipSpace(); } while (Take(","));
                if (!Take(")")) throw new FormatException();
            }

            return new Call(name, arguments);
        }

        private void SkipSpace()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }

        private bool Peek(string token)

        {

            SkipSpace();

            return string.CompareOrdinal(text, _at, token, 0, token.Length) == 0;

        }


        private bool Take(string token)
        {
            SkipSpace();
            if (!text.AsSpan(_at).StartsWith(token)) return false;

            _at += token.Length;
            return true;
        }

        /// <summary>Takes a word only when it is whole, so <c>ifSomething</c> is a name.</summary>
        private bool TakeWord(string word)
        {
            SkipSpace();
            if (!text.AsSpan(_at).StartsWith(word)) return false;

            int after = _at + word.Length;
            if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_')) return false;

            _at = after;
            return true;
        }

        /// <summary>
        /// A name, or null without consuming anything. Names may begin with digits
        /// -- <c>1stPRot</c> is a real variable -- so a leading run of digits is a
        /// name when a letter or underscore follows it and a number when it does not.
        /// </summary>
        private string? ReadName(bool dotted = false)
        {
            SkipSpace();
            int start = _at, at = _at;

            while (at < text.Length && char.IsDigit(text[at])) at++;

            if (at >= text.Length || (!char.IsLetter(text[at]) && text[at] != '_')) return null;

            _at = at;
            while (_at < text.Length &&
                   (char.IsLetterOrDigit(text[_at]) || text[_at] == '_' ||
                    (dotted && text[_at] == '.'))) _at++;

            return text[start.._at];
        }
    }
}
