package com.horine.emailAttachmentDownloader;

public class Main {

    EmailGetter getter;
    GlobalSettings settings;
    public Main(){
        settings = new GlobalSettings("settings.cfg");
        settings.getData();
        settings.setKeywords("pictures,lori,bryce,test,attachments,calvin,clara,inline,slideshow");

        ImageSaver imageSaver = new ImageSaver(settings.getSaveFolder());
        getter = new EmailGetter(settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(), imageSaver);
        getter.fetch(settings.getKeywords());
        settings.saveData();

    }
    public static void main(String args[]){
        new Main();
    }
}