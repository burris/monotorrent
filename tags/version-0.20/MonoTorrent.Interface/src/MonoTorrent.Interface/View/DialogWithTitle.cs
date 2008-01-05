/*
 * $Id: DialogWithTitle.cs 880 2006-08-19 22:50:54Z piotr $
 * Copyright (c) 2006 by Piotr Wolny <gildur@gmail.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

using System;

using Gtk;

namespace MonoTorrent.Interface.View
{
    public class DialogWithTitle : Dialog
    {
        private Box layout;

        protected Box Layout
        {
            get {
                return this.layout;
            }
        }

        public DialogWithTitle(string title, Window parent) : base(
                title, parent,
                DialogFlags.Modal | DialogFlags.DestroyWithParent)
        {
            Label titleLabel = new Label("<b><u><span size='large'>" + Title
                    + "</span></u></b>");
            titleLabel.UseMarkup = true;
            titleLabel.Xalign = 0;

            this.layout = new VBox();
            this.layout.BorderWidth = 10;
            this.layout.Spacing = 5;

            this.layout.PackStart(titleLabel);
            VBox.PackStart(this.layout);
        }
    }
}
