package com.horine.emailAttachmentDownloader;


import java.io.File;
import java.util.ArrayList;

public class Main {


    private Main(){
        ArrayList<GlobalSettings> settingsFiles = new ArrayList<GlobalSettings>();
        File[] files = new File("cfg").listFiles();
        for (File file : files) {
            if (file.isFile() && file.getName().substring(file.getName().lastIndexOf(".")).equals(".cfg")) {
                GlobalSettings newSettings = new GlobalSettings(file.getPath());
                newSettings.getData();
                settingsFiles.add(newSettings);
            }
        }
        System.out.println(settingsFiles.size());
        DisplayPage displayPage = new DisplayPage(settingsFiles);

        for (GlobalSettings settings : settingsFiles) {
            if (settings.getOnstartup()) {
                if (settings.getSaveFolder() == null) {
                    settings.setSaveFolder(displayPage.chooseFolder());
                }
                ImageSaver imageSaver = new ImageSaver(settings.getSaveFolder());
                EmailGetter getter = new EmailGetter(settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(), imageSaver);
                DisplayElem elem = getter.fetch(settings.getKeywords());
                displayPage.addElement(elem);
                settings.saveData();
            }
        }
    }
    public static void main(String[] args){
        new Main();
    }
}