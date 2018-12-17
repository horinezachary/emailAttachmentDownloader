package com.horine.emailAttachmentDownloader;

public class Main {

    EmailGetter getter;
    GlobalSettings settings;
    DisplayPage displayPage;

    public Main(){
        settings = new GlobalSettings("settings.cfg");
        settings.getData();

        ImageSaver imageSaver = new ImageSaver(settings.getSaveFolder());
        getter = new EmailGetter(settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(), imageSaver);

        displayPage = new DisplayPage(settings,getter);
        displayPage.addElement(new DisplayElem(displayPage, 0,"THIS IS A TEST"));

        if (settings.getSaveFolder() == null) {
            settings.setSaveFolder(displayPage.chooseFolder());
        }

        getter.fetch(settings.getKeywords());
        settings.saveData();

    }
    public static void main(String args[]){
        new Main();
    }
}