using Soneta.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace ImportPAYU
{
    public class Element
    {
        public string ID { get; set; }
        public Date Data { get; set; }
        public Currency Kwota { get; set; }
        public string Prowizja { get; set; }
        public string Wyplata { get; set; }
        public string Opis { get; set; }
        public string Kupujacy { get; set; }
        public string Numer_zamowienia { get; set; }

        public Element(string id, string data, string kwota, string prowizja, string wyplata, string opis, string kupujacy, string numer_zamowienia)
        {
            this.ID = id;
            this.Data = new Date(Convert.ToInt16(data.Split('.')[2]), Convert.ToInt16(data.Split('.')[1]), Convert.ToInt16(data.Split('.')[0]));
            Currency kwota_zamowienia = new Currency(System.Convert.ToDouble(kwota), "PLN");
            this.Kwota = kwota_zamowienia;
            this.Prowizja = prowizja;
            this.Wyplata = wyplata;
            this.Opis = opis;
            this.Kupujacy = kupujacy;
            this.Numer_zamowienia = numer_zamowienia;
        }
    }
}
