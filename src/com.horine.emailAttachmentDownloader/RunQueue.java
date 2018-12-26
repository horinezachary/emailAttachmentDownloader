package com.horine.emailAttachmentDownloader;

import java.util.ArrayList;
import java.util.LinkedList;
import java.util.Queue;

public class RunQueue implements Runnable {

    private DisplayPage displayPage;
    private Queue<GlobalSettings> queue;
    private ArrayList<GlobalSettings> settingsFiles;
    private Thread t;
    private boolean running;
    private long lastCheck;
    private long checkTime;

    RunQueue(ArrayList<GlobalSettings> settingsFiles){
        this.settingsFiles = settingsFiles;
        this.displayPage = null;
        queue = new LinkedList<GlobalSettings>();
        running = false;
        checkTime = 60000;
        checkTime = 10;
        t = new Thread(this);
    }

    public void setDisplayPage(DisplayPage displayPage) {
        this.displayPage = displayPage;
    }

    void add(GlobalSettings settings){
        queue.add(settings);
        System.out.println(t.getState());
        if (!t.getState().name().equals("RUNNABLE")) {
            t.start();
        }
    }

    @Override
    public void run() {
        running = true;
        while (running) {
            //System.out.println(queue.peek().getFileName());
            if (queue.peek() != null) {
                GlobalSettings settings = queue.poll();
                System.out.println(settings.getFileName());
                ImageSaver imageSaver = new ImageSaver(settings.getSaveFolder());
                EmailGetter getter = new EmailGetter(settings.getFileName(), settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(), imageSaver);
                DisplayElem email = getter.fetch(settings.getKeywords());
                displayPage.addElement(email);
                settings.setLastRuntime(System.currentTimeMillis());
            }
            /*else{
                System.out.println("NULL");
            }*/
            for (GlobalSettings settings: settingsFiles){
                if (settings.getScheduled()) {
                    if (settings.getLastRuntime() + settings.getSchedule() * settings.getTimeMultuplier() <= System.currentTimeMillis()) {
                        queue.add(settings);
                    }
                }
            }
        }
    }
}
