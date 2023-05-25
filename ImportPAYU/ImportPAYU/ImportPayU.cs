using Soneta.Business;
using Soneta.Business.UI;
using Soneta.CRM;
using Soneta.Handel;
using Soneta.Kasa;
using Soneta.Magazyny;
using Soneta.Produkcja;
using Soneta.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

[assembly: Worker(typeof(ImportPAYU.ImportPayU), typeof(RaportESP))]

namespace ImportPAYU
{
    public class ImportPayU
    {
        [Context, Required]
        public NamedStream CsvFileName { get; set; }

        RaportESP raport;
        [Context]
        public RaportESP Raport
        {
            get { return raport; }
            set { raport = value; }
        }

        [Context]
        public Context Context { get; set; }

        [Action("Importuj wpłaty z pliku",
        Target = ActionTarget.ToolbarWithText,
        Priority = 1001,
        Icon = ActionIcon.ArrowDown,
        Mode = ActionMode.SingleSession)]
        public MessageBoxInformation Function()
        {
            string operacja = "";
            string data = "";
            string kwota = "";
            string id = "";
            string kupujacy = "";
            int dodanych_dok = 0;
            int numer_linii_w_pliku = 1;
            string problem = "";
            bool czy_problem = false;
            List<Element> csvList = new List<Element>();

            using (var reader = new StreamReader(CsvFileName.FileName))
            {
                int temp = 0;
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var komorka = line.Split(new[] { ";" }, StringSplitOptions.None);

                    operacja = ""; data = ""; kwota = ""; id = ""; kupujacy = "";
                    if (line != null && line != "" && temp != 0)
                    {
                        var fragments = Regex.Split(line, "\",");
                        id = fragments[2].ToString().Replace("\"", "");
                        data = fragments[0].Replace("\"", "").Split(' ')[0];
                        kwota = fragments[8].Replace("\"", "").Split('z')[0].Replace(".", ",").Trim();
                        operacja = fragments[3].Replace("\"", "");
                        if (!kwota.Contains("-"))
                            kupujacy = fragments[5].Replace("\"", "").Split(';')[1];
                        csvList.Add(new Element(id, data, kwota, "", "", operacja, kupujacy, ""));
                    }
                    temp++;
                }

                using (Session session = raport.Session.Login.CreateSession(false, false))
                {
                    KasaModule km = KasaModule.GetInstance(session);
                    CRMModule cm = CRMModule.GetInstance(session);
                    HandelModule handel_module = HandelModule.GetInstance(session);
                    MagazynyModule mm = MagazynyModule.GetInstance(session);

                    EwidencjaSP kasa = raport.Kasa;
                    FromTo okres = raport.Okres;
                    Date data1 = raport.Data;
                    RaportESP rap = km.RaportyESP[raport.ID];

                    Magazyn magazyn_wysylkowy = mm.Magazyny.WgSymbol["F"];

                    DefDokHandlowego def = handel_module.DefDokHandlowych.WgSymbolu["FVI"];
                    DefDokHandlowego def2 = handel_module.DefDokHandlowych.WgSymbolu["FFI"];


                    using (ITransaction t = session.Logout(true))
                    {

                        foreach (Element element in csvList)
                        {
                            numer_linii_w_pliku++;
                            try
                            {
                                Wplata wplata = null;
                                Naleznosc naleznosc = null;
                                if (element.Opis == "wpłata")
                                {
                                    bool pominac = false;

                                    Soneta.Business.View view = handel_module.DokHandlowe.CreateView();
                                    view.Condition &= new FieldCondition.In("Definicja", def, def2);
                                    view.Condition &= new FieldCondition.Equal("Magazyn", magazyn_wysylkowy);
                                    view.Condition &= new FieldCondition.Equal("Features.paymentId", element.ID);
                                    DokumentHandlowy dokument_handlowy = (DokumentHandlowy)view.GetFirst();


                                    // sprawdzanie czy dokument istnieje z cechą paymentId
                                    if (view.Count == 0)
                                    {
                                        problem += "Brak FFI z PaymentID: " + element.ID + Environment.NewLine;
                                        czy_problem = true;
                                        continue;
                                    }


                                    //walidacja w raporcie
                                    foreach (var wr in rap.Zaplaty)
                                    {
                                        DokumentHandlowy temp_dok = temp_dok = handel_module.DokHandlowe.WgNumer[wr.NumeryDokumentow];

                                        if (temp_dok != null)
                                            if (wr.Kwota == element.Kwota && temp_dok.Features["paymentId"].ToString() == element.ID)
                                                pominac = true;
                                    }

                                    // walidacja daty
                                    if (element.Data.Month != rap.Data.Month)
                                        pominac = true;

                                    if (pominac)
                                        continue;




                                    WplataRaport dok = new WplataRaport(rap);
                                    km.Zaplaty.AddRow(dok);

                                    dok.Podmiot = cm.Kontrahenci.WgKodu["PAYU"];
                                    dok.Kwota = element.Kwota;
                                    dok.Opis = element.Kupujacy + " " + dokument_handlowy.Numer.Pelny;

                                    foreach (DokumentHandlowy dok_fak in view)
                                    {
                                        if (dok_fak.Features["paymentId"].ToString() == element.ID)
                                        {
                                            dok.NumeryDokumentow = dok_fak.Numer.NumerPelny;
                                            break;
                                        }
                                    }


                                    Kontrahent temp_kontr = cm.Kontrahenci.WgKodu["PAYU"];
                                    SubTable st = km.RozrachunkiIdx.WgPodmiot[temp_kontr, Date.MaxValue];

                                    foreach (RozrachunekIdx idx in st)
                                    {
                                        if (idx.Typ == TypRozrachunku.Wpłata && wplata == null)
                                            wplata = (Wplata)idx.Dokument;
                                        if (idx.Typ == TypRozrachunku.Należność && naleznosc == null && !idx.Dokument.Bufor && idx.Numer == dokument_handlowy.Numer.Pelny)
                                            naleznosc = (Naleznosc)idx.Dokument;
                                        if (wplata != null && naleznosc != null)
                                            break;
                                    }

                                    try
                                    {
                                        wplata.DataDokumentu = element.Data;

                                        RozliczenieSP rozliczenie = new RozliczenieSP(naleznosc, (Wplata)dok);
                                        km.RozliczeniaSP.AddRow(rozliczenie);
                                    }
                                    catch(Exception ex) { }
                                    dodanych_dok++;

                                }
                            }
                            catch (Exception e)
                            {
                                //problem += "PaymentID: " + element.ID + " wiersz: " + numer_linii_w_pliku.ToString() + Environment.NewLine;
                                //czy_problem = true;
                            }

                        }


                        t.Commit();
                    }
                    session.Save();
                }
            }
            return new MessageBoxInformation("123", "456");
        }
    }
}
