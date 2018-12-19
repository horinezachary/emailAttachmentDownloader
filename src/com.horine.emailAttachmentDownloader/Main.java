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
        displayPage.addElement(new DisplayElem(displayPage, 0,"THIS IS A TEST1"));
        displayPage.addElement(new DisplayElem(displayPage, 1,"THIS IS A TEST2"));
        displayPage.addElement(new DisplayElem(displayPage, 2,"THIS IS A TEST3"));
        displayPage.addElement(new DisplayElem(displayPage, 3,"THIS IS A TEST4"));
        displayPage.addElement(new DisplayElem(displayPage, 4,"THIS IS A TEST5"));


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