﻿using CMI.Utilities.Common;

namespace CMI.Manager.Vecteur.Properties
{
    public class Documentation : AbstractDocumentation
    {
        public override void LoadDescriptions()
        {
            AddDescription<VecteurSettings>(x => x.Address, "URL des API für Digitalisierungsaufträge");
            AddDescription<VecteurSettings>(x => x.ApiKey, "API-Key des API für Digitalisierungsaufträge");
            AddDescription<VecteurSettings>(x => x.BaseDirectory, "Lokales Verzeichnis des VecteurService");
            AddDescription<VecteurSettings>(x => x.SftpPassword, "Passwort des SFTP des API für Digitalisierungsaufträge");
            AddDescription<VecteurSettings>(x => x.SftpPort, "Port des SFTP des API für Digitalisierungsaufträge");
            AddDescription<VecteurSettings>(x => x.RequestTimeoutInMinute, "Request Timeout für Digitalisierungsaufträge");
        }
    }
}