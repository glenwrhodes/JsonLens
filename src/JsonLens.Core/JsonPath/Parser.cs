using System.Globalization;
using System.Text;

namespace JsonLens.Core.JsonPath;

/// <summary>Recursive-descent parser for the RFC 9535 subset described on <see cref="JsonPathQuery"/>.</summary>
internal sealed class Parser
{
    private readonly string _s;
    private int _pos;

    public Parser(string text) => _s = text;

    private bool AtEnd => _pos >= _s.Length;
    private char Peek(int offset = 0) => _pos + offset < _s.Length ? _s[_pos + offset] : '\0';

    private JsonPathException Error(string message, int? position = null) =>
        new(message, Math.Min(position ?? _pos, _s.Length));

    private string Describe() => AtEnd ? "end of query" : $"'{_s[_pos]}'";

    public QueryAst ParseRootQuery()
    {
        SkipWs();
        if (AtEnd) throw Error("The query is empty. Start with $, for example $.name");
        if (Peek() != '$') throw Error("A query must start with $ (the root), for example $.store.book[0]");
        _pos++;
        var segments = ParseSegments();
        SkipWs();
        if (!AtEnd) throw Error($"Unexpected {Describe()}");
        return new QueryAst(relative: false, segments);
    }

    private Segment[] ParseSegments()
    {
        var segments = new List<Segment>();
        while (true)
        {
            int save = _pos;
            SkipWs();
            char c = Peek();
            if (c == '[')
            {
                segments.Add(new Segment(false, ParseBracketed()));
            }
            else if (c == '.' && Peek(1) == '.')
            {
                _pos += 2;
                if (Peek() == '[') segments.Add(new Segment(true, ParseBracketed()));
                else if (Peek() == '*') { _pos++; segments.Add(new Segment(true, new Selector[] { WildcardSelector.Instance })); }
                else if (PathFormat.IsNameFirst(Peek())) segments.Add(new Segment(true, new Selector[] { new NameSelector(ParseShorthandName()) }));
                else throw Error($"Expected a name, * or [ after '..' but found {Describe()}");
            }
            else if (c == '.')
            {
                _pos++;
                if (Peek() == '*') { _pos++; segments.Add(new Segment(false, new Selector[] { WildcardSelector.Instance })); }
                else if (PathFormat.IsNameFirst(Peek())) segments.Add(new Segment(false, new Selector[] { new NameSelector(ParseShorthandName()) }));
                else if (char.IsDigit(Peek())) throw Error("Names can't start with a digit after '.'; use [n] for an index or ['name'] for a name");
                else throw Error($"Expected a name or * after '.' but found {Describe()}");
            }
            else
            {
                _pos = save;
                return segments.ToArray();
            }
        }
    }

    private string ParseShorthandName()
    {
        int start = _pos;
        while (!AtEnd && (PathFormat.IsNameFirst(Peek()) || char.IsAsciiDigit(Peek()))) _pos++;
        return _s[start.._pos];
    }

    private Selector[] ParseBracketed()
    {
        int open = _pos;
        _pos++; // '['
        var selectors = new List<Selector>();
        while (true)
        {
            SkipWs();
            if (AtEnd) throw Error("Missing ] to close the [ here", open);
            selectors.Add(ParseSelector());
            SkipWs();
            if (Peek() == ',') { _pos++; continue; }
            if (Peek() == ']') { _pos++; break; }
            if (AtEnd) throw Error("Missing ] to close the [ here", open);
            throw Error($"Expected , or ] but found {Describe()}");
        }
        return selectors.ToArray();
    }

    private Selector ParseSelector()
    {
        char c = Peek();
        if (c == '\'' || c == '"') return new NameSelector(ParseStringLiteral());
        if (c == '*') { _pos++; return WildcardSelector.Instance; }
        if (c == '?') { _pos++; return new FilterSelector(ParseLogicalOr()); }
        if (c == '-' || char.IsAsciiDigit(c) || c == ':') return ParseIndexOrSlice();
        if (c == ']') throw Error("Empty brackets: expected an index, 'name', *, a slice or a ?filter");
        if (PathFormat.IsNameFirst(c)) throw Error("Names inside brackets need quotes, for example ['name']");
        throw Error($"Expected an index, 'name', *, a slice or a ?filter but found {Describe()}");
    }

    private Selector ParseIndexOrSlice()
    {
        long? start = TryParseInt();
        SkipWs();
        if (Peek() != ':')
        {
            if (start is null) throw Error($"Expected an index but found {Describe()}");
            return new IndexSelector(start.Value);
        }
        _pos++;
        SkipWs();
        long? end = TryParseInt();
        SkipWs();
        long? step = null;
        if (Peek() == ':')
        {
            _pos++;
            SkipWs();
            step = TryParseInt();
        }
        return new SliceSelector(start, end, step);
    }

    private long? TryParseInt()
    {
        int start = _pos;
        if (Peek() == '-') _pos++;
        int digitsStart = _pos;
        while (char.IsAsciiDigit(Peek())) _pos++;
        if (_pos == digitsStart)
        {
            if (_pos != start) throw Error("Expected digits after '-'");
            return null;
        }
        // RFC 9535: int = "0" / (["-"] DIGIT1 *DIGIT), so no leading zeros and no "-0".
        if (_s[digitsStart] == '0' && _pos - digitsStart > 1)
            throw Error("Numbers can't have a leading zero", start);
        if (_s[digitsStart] == '0' && digitsStart != start)
            throw Error("-0 isn't a valid index; use 0", start);
        var text = _s[start.._pos];
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            value > 9007199254740991 || value < -9007199254740991)
            throw Error($"The number {text} is too large", start);
        return value;
    }

    // ------------------------------------------------------------ filters

    private LogicalExpr ParseLogicalOr()
    {
        var items = new List<LogicalExpr> { ParseLogicalAnd() };
        while (true)
        {
            SkipWs();
            if (Peek() == '|' && Peek(1) == '|') { _pos += 2; items.Add(ParseLogicalAnd()); }
            else break;
        }
        return items.Count == 1 ? items[0] : new OrExpr(items.ToArray());
    }

    private LogicalExpr ParseLogicalAnd()
    {
        var items = new List<LogicalExpr> { ParseBasic() };
        while (true)
        {
            SkipWs();
            if (Peek() == '&' && Peek(1) == '&') { _pos += 2; items.Add(ParseBasic()); }
            else break;
        }
        return items.Count == 1 ? items[0] : new AndExpr(items.ToArray());
    }

    private LogicalExpr ParseBasic()
    {
        SkipWs();
        char c = Peek();

        if (c == '!' && Peek(1) != '=')
        {
            _pos++;
            SkipWs();
            if (Peek() == '(') return new NotExpr(ParseParen());
            if (Peek() == '@' || Peek() == '$')
            {
                var q = ParseFilterQuery();
                SkipWs();
                if (IsComparisonAhead())
                    throw Error("Put the comparison in parentheses to negate it, for example !(@.a == 1)");
                return new NotExpr(new ExistsExpr(q));
            }
            throw Error($"Expected ( or a query after '!' but found {Describe()}");
        }

        if (c == '(') return ParseParen();

        int leftPos = _pos;
        Comparable left;
        if (c == '@' || c == '$')
        {
            var q = ParseFilterQuery();
            SkipWs();
            if (!IsComparisonAhead()) return new ExistsExpr(q);
            left = new QueryComparable(q);
        }
        else
        {
            left = ParseComparableOrError();
            SkipWs();
            if (!IsComparisonAhead())
                throw Error("A value on its own isn't a test; compare it, for example @.price < 10", leftPos);
        }

        var op = ParseComparisonOp();
        SkipWs();
        var right = ParseComparableOrError();
        return new ComparisonExpr(left, op, right);
    }

    private LogicalExpr ParseParen()
    {
        int open = _pos;
        _pos++; // '('
        var inner = ParseLogicalOr();
        SkipWs();
        if (Peek() != ')') throw Error(AtEnd ? "Missing ) to close the ( here" : $"Expected ) but found {Describe()}", AtEnd ? open : null);
        _pos++;
        return inner;
    }

    private QueryAst ParseFilterQuery()
    {
        bool relative = Peek() == '@';
        _pos++;
        return new QueryAst(relative, ParseSegments());
    }

    private bool IsComparisonAhead()
    {
        char c = Peek();
        if (c == '=' || c == '<' || c == '>') return true;
        return c == '!' && Peek(1) == '=';
    }

    private CompareOp ParseComparisonOp()
    {
        char c = Peek(), d = Peek(1);
        int at = _pos;
        if (c == '=' && d == '=') { _pos += 2; return CompareOp.Eq; }
        if (c == '!' && d == '=') { _pos += 2; return CompareOp.Ne; }
        if (c == '<' && d == '=') { _pos += 2; return CompareOp.Le; }
        if (c == '>' && d == '=') { _pos += 2; return CompareOp.Ge; }
        if (c == '<') { _pos++; return CompareOp.Lt; }
        if (c == '>') { _pos++; return CompareOp.Gt; }
        if (c == '=') throw Error("Use == to compare", at);
        throw Error($"Expected a comparison (== != < <= > >=) but found {Describe()}", at);
    }

    private Comparable ParseComparableOrError()
    {
        char c = Peek();
        if (c == '@' || c == '$') return new QueryComparable(ParseFilterQuery());
        if (c == '\'' || c == '"') return new LiteralComparable(FValue.FromString(ParseStringLiteral()));
        if (c == '-' || char.IsAsciiDigit(c)) return new LiteralComparable(FValue.FromNumber(ParseNumberLiteral()));
        if (char.IsAsciiLetter(c))
        {
            int start = _pos;
            while (char.IsAsciiLetterOrDigit(Peek()) || Peek() == '_') _pos++;
            var word = _s[start.._pos];
            int afterWord = _pos;
            SkipWs();
            if (Peek() == '(')
                throw Error($"Functions such as {word}() aren't supported", start);
            _pos = afterWord;
            switch (word)
            {
                case "true": return new LiteralComparable(FValue.True);
                case "false": return new LiteralComparable(FValue.False);
                case "null": return new LiteralComparable(FValue.Null);
            }
            throw Error($"Unknown word '{word}'; strings need quotes, for example '{word}'", start);
        }
        if (AtEnd) throw Error("The filter ends too early; expected a value");
        throw Error($"Expected a value (number, 'string', true, false, null or a query) but found {Describe()}");
    }

    private double ParseNumberLiteral()
    {
        int start = _pos;
        if (Peek() == '-') _pos++;
        if (!char.IsAsciiDigit(Peek())) throw Error("Expected digits in number", start);
        // RFC 9535 (like JSON): the integer part is "0" or starts with 1-9; "-0" is allowed.
        if (Peek() == '0' && char.IsAsciiDigit(Peek(1)))
            throw Error("Numbers can't have a leading zero", start);
        while (char.IsAsciiDigit(Peek())) _pos++;
        if (Peek() == '.')
        {
            _pos++;
            if (!char.IsAsciiDigit(Peek())) throw Error("Expected digits after the decimal point");
            while (char.IsAsciiDigit(Peek())) _pos++;
        }
        if (Peek() == 'e' || Peek() == 'E')
        {
            _pos++;
            if (Peek() == '+' || Peek() == '-') _pos++;
            if (!char.IsAsciiDigit(Peek())) throw Error("Expected digits in the exponent");
            while (char.IsAsciiDigit(Peek())) _pos++;
        }
        return double.Parse(_s.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private string ParseStringLiteral()
    {
        int open = _pos;
        char quote = Peek();
        _pos++;
        var sb = new StringBuilder();
        while (true)
        {
            if (AtEnd) throw Error($"Missing closing {quote} for the string that starts here", open);
            char c = _s[_pos++];
            if (c == quote) return sb.ToString();
            if (c != '\\')
            {
                if (c < 0x20) throw Error("Control characters must be escaped in strings", _pos - 1);
                sb.Append(c);
                continue;
            }
            if (AtEnd) throw Error($"Missing closing {quote} for the string that starts here", open);
            char e = _s[_pos++];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '/': sb.Append('/'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                case 'u':
                    if (_pos + 4 > _s.Length ||
                        !int.TryParse(_s.AsSpan(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        throw Error("\\u must be followed by 4 hex digits", _pos - 2);
                    sb.Append((char)code);
                    _pos += 4;
                    break;
                default:
                    throw Error($"Unknown escape \\{e}", _pos - 2);
            }
        }
    }

    private void SkipWs()
    {
        while (!AtEnd && (_s[_pos] == ' ' || _s[_pos] == '\t' || _s[_pos] == '\n' || _s[_pos] == '\r')) _pos++;
    }
}
