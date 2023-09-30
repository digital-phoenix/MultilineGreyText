using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;

namespace MultilineGreyText
{
    internal sealed class MultilineGreyTextTagger : ITagger<TestTag>
    {
        /// <summary>
        /// panel with multiline grey text
        /// </summary>
        private StackPanel stackPanel;

        /// <summary>
        /// used to set the colour of the grey text
        /// </summary>
        private Brush greyBrush;

        /// <summary>
        /// used to set the colour of text that overlaps with the users text
        /// </summary>
        private Brush transparentBrush;

        /// <summary>
        /// suggestion to display in multiline 
        /// first string is to match against second item: array is for formatting
        /// </summary>
        Tuple<String, String[]> suggestion;

        /// <summary>
        /// contains the editor text and OnChange triggers on any text changes
        /// </summary>

        ITextBuffer buffer;

        /// <summary>
        /// current editor display, immutable data
        /// </summary>
        ITextSnapshot snapshot;

        /// <summary>
        /// the editor display object
        /// </summary>
        IWpfTextView view;

        /// <summary>
        /// contains the grey text
        /// </summary>
        private IAdornmentLayer adornmentLayer;

        /// <summary>
        /// true if a suggestion should be shown
        /// </summary>
        private bool showSuggestion = false;

        /// <summary>
        ///  line number the suggestion should be displayed at
        /// </summary>
        private int currentLineN;

        //Constructor
        public MultilineGreyTextTagger(IWpfTextView view, ITextBuffer buffer)
        {
            String suggestionText = "int sum(int a, int b)\n{\n\treturn a + b;\n}";
            String[] lines = suggestionText.Split('\n');
            suggestion = new Tuple<string, string[]>(suggestionText, lines);
            this.stackPanel = new StackPanel();

            this.buffer = buffer;
            this.snapshot = buffer.CurrentSnapshot;
            this.buffer.Changed += BufferChanged;
            this.view = view;
            this.adornmentLayer = view.GetAdornmentLayer("MultilineGreyText");

            this.view.LayoutChanged += this.OnSizeChanged;

            this.transparentBrush = new SolidColorBrush();
            this.transparentBrush.Opacity = 0;
            this.greyBrush = new SolidColorBrush(Colors.Gray);
        }

        //This an iterator that is used to iterate through all of the test tags
        //tags are like html tags they mark places in the view to modify how those sections look
        //Testtag is a tag that tells the editor to add empty space
        public IEnumerable<ITagSpan<TestTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
           if (!showSuggestion)
            {
                yield break;
            }

            SnapshotSpan entire = new SnapshotSpan(spans[0].Start, spans[spans.Count - 1].End).TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);

            ITextSnapshot currentSnapshot = spans[0].Snapshot;

            var line = currentSnapshot.GetLineFromLineNumber(currentLineN).Extent;
            var span = line.TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);

            stackPanel.Measure(new Size(Double.MaxValue, Double.MaxValue));
        
            //on start up height isn't always available
            var height = stackPanel.ActualHeight > 0 ? stackPanel.ActualHeight : 60.426666666666669;

            yield return new TagSpan<TestTag>(
                new SnapshotSpan(line.End, line.End),
                new TestTag(0, 0, 0, 0, height, PositionAffinity.Predecessor, stackPanel, this));
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        //triggers when the editor text buffer changes
        void BufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // If this isn't the most up-to-date version of the buffer, then ignore it for now (we'll eventually get another change event).
            if (e.After != buffer.CurrentSnapshot)
                return;
            this.Update();
        }

        //used to set formatting of the displayed multi lines
        public void FormatText(TextBlock block)
        {
            var pos = snapshot.GetLineFromLineNumber(currentLineN).Start;
            var format = view.TextViewLines.WpfTextViewLines[currentLineN].GetCharacterFormatting(pos);
            block.FontFamily = format.Typeface.FontFamily;
            block.FontSize = format.FontRenderingEmSize;
        }

        //Updates the grey text
        public void UpdateAdornment(IWpfTextView view, string userText, String[] suggestion, int suggestionStart)
        {

            stackPanel.Children.Clear();
            for (int i = suggestionStart; i < suggestion.Length; i++)
            {
                String line = suggestion[i];

                TextBlock textBlock = new TextBlock();

                if (i == 0)
                {
                    textBlock.Inlines.Add(item:
                        new Run(userText) { Foreground = transparentBrush }) ;

                    int length = userText.Trim().Length;
                    int offset = line.Length - line.TrimStart().Length;

                    string remainder = line.Substring(length + offset);
                    textBlock.Inlines.Add(item: new Run(remainder) { Foreground = greyBrush });
                }
                else
                {
                    textBlock.Inlines.Add(item: new Run(line));

                    textBlock.Foreground = new SolidColorBrush(Colors.Gray);
                }

                textBlock.FontStyle = FontStyles.Normal;
                textBlock.FontWeight = FontWeights.Normal;

                stackPanel.Children.Add(textBlock);
            }
        }

        //Adds grey text to display
        private void OnSizeChanged(object sender, EventArgs e)
        {
            if(!showSuggestion || currentLineN >= view.TextViewLines.Count) return;

            foreach (TextBlock block in stackPanel.Children)
            {
                    FormatText(block);
            }

            // Clear the adornment layer of previous adornments
            this.adornmentLayer.RemoveAllAdornments();

            //usually only happens the moment a bunch of text has rentered such as an undo operation

            ITextSnapshotLine snapshotLine = view.TextSnapshot.GetLineFromLineNumber(currentLineN);
            var start = view.TextViewLines.GetCharacterBounds(snapshotLine.Start);

            // Place the image in the top left hand corner of the line
            Canvas.SetLeft(stackPanel, start.Left);
            Canvas.SetTop(stackPanel, start.Top);

            var span = snapshotLine.Extent;

            // Add the image to the adornment layer and make it relative to the viewport
            this.adornmentLayer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, stackPanel, null);
        }

        //returns the number of times letter c appears in s
        int GetOccurenceOfLetter(String s, char c)
        { 
            int n = 0;
            for (int i = 0; (i = s.IndexOf(c, i)) >= 0; i++, n++){}
            return n;
        }

        //Check if the text in the editor is a substring of the the suggestion text 
        //If it matches return the line number of the suggestion text that matches the current line in the editor 
        //else return -1
        int CheckSuggestion(ITextSnapshot newSnapshot, String suggestion, String line, int startLine)
        {
            if(line.Length == 0)
            {
                line = newSnapshot.GetLineFromLineNumber(startLine - 1).GetText().Trim();
                line += "\n";
            }

            int index = suggestion.IndexOf(line);
            int endPos = index + line.Length;

            if (index > -1)
            {
                int firstLineBreak = suggestion.IndexOf('\n');
                if (firstLineBreak == -1 || endPos < firstLineBreak)
                {
                    return index == 0 ? 0 : -1;
                }
                else
                {
                    int nLines = GetOccurenceOfLetter(suggestion.Substring(0, endPos), '\n');

                    if (startLine >= nLines)
                    {
                        string fullText = "";
                        for (int i = startLine - nLines; i <= startLine; i++)
                        {
                            fullText += newSnapshot.GetLineFromLineNumber(i).GetText().Trim() + "\n";
                        }
                        return suggestion.IndexOf(fullText.TrimEnd()) > -1 ? nLines : -1;
                    }
                }

            }

            return -1;
        }

        //gets the string to display from format text 
        string GetSuggestionTextFromPosition(string[] suggestion, int start) {
            String s = "";
            for (int i = start; i < suggestion.Length; i++)
            {
                s += suggestion[i] + "\n";
            }

            return s;
        }

        //update multiline data
        void Update(){
            //get line carat is on
            //if suggestion matches line (possibly including preceding lines)
            //  show suggestion
            //else
            //  clear suggestions
        
            ITextSnapshot newSnapshot = buffer.CurrentSnapshot;
            this.snapshot = newSnapshot;
     
            CaretPosition caretPosition = view.Caret.Position;

            var point = caretPosition.Point.GetPoint(buffer, caretPosition.Affinity);

            if (!point.HasValue)
            {
                return;
            }
            
            int lineN = newSnapshot.GetLineNumberFromPosition(point.Value);
                
            String untrimLine = newSnapshot.GetLineFromLineNumber(lineN).GetText();
            String line = untrimLine.Trim();

            //dont attempt to match empty lines unless a suggestion is already shown
            if(line.Length == 0){
                if (!showSuggestion)
                {
                    return;
                }else if(lineN == 0)
                {
                    ClearSuggestion();
                    return;
                }
            }

            //get the line in the suggestion that matches the user input
            int suggestionLineN = CheckSuggestion(newSnapshot, suggestion.Item1, line, lineN);

            if (suggestionLineN  >= 0)
            {
                if (untrimLine.Length > 0 && untrimLine[untrimLine.Length - 1] == '\t')
                {
                    String suggestion = GetSuggestionTextFromPosition(this.suggestion.Item2, suggestionLineN);

                    ReplaceText(suggestion, lineN);
                }
                else
                {
                    ShowSuggestion(line, suggestion.Item2, suggestionLineN, lineN);
                }
            }
            else
            {
                ClearSuggestion();
            }
        }

        //replaces text in the editor
        void ReplaceText(string suggestion, int lineN)
        {
            SnapshotSpan span = this.snapshot.GetLineFromLineNumber(lineN).Extent;
            ITextEdit edit = view.TextBuffer.CreateEdit();

            edit.Replace(span, suggestion);
            edit.Apply();
            ClearSuggestion();
        }

        //sets up the suggestion for display
        void ShowSuggestion(String text, String[] suggestion, int suggestionLineStart, int line)
        {
            this.currentLineN = line;

            UpdateAdornment(view, text, suggestion, suggestionLineStart);
            showSuggestion = true;
            MarkDirty();
        }

        //removes the suggestion
        void ClearSuggestion()
        {
            if(!showSuggestion) return;

            adornmentLayer.RemoveAllAdornments();
            showSuggestion = false;

            MarkDirty();
        }

        //triggers refresh of the screen 
        void MarkDirty(){
            ITextSnapshot newSnapshot = buffer.CurrentSnapshot;
            this.snapshot = newSnapshot;
           
            if(view.TextViewLines == null)
            {
                return;
            }
            var changeStart = view.TextViewLines.FirstVisibleLine.Start;
            var changeEnd = view.TextViewLines.LastVisibleLine.Start;

            var startLine = view.TextSnapshot.GetLineFromPosition(changeStart);
            var endLine = view.TextSnapshot.GetLineFromPosition(changeEnd);

            var span = new SnapshotSpan(startLine.Start, endLine.EndIncludingLineBreak).
                TranslateTo(newSnapshot, SpanTrackingMode.EdgePositive);

            //lines we are marking dirty
            //currently all of them for simplicity 
            if (this.TagsChanged != null)
            {
                this.TagsChanged(this, new SnapshotSpanEventArgs(span)); 
            }
        }
    }
}