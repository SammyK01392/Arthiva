using System;
using System.Collections.Generic;
using System.Text;

namespace MoneySpend.Views
{
    public class AddPlaceholderPage : ContentPage
    {
        public AddPlaceholderPage()
        {
            Content = new Grid(); // Shell ko content chahiye, warna crash
        }
    }
}
