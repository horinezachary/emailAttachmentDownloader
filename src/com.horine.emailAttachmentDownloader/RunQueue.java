package com.horine.emailAttachmentDownloader;

import java.util.LinkedList;
import java.util.Queue;

public class RunQueue implements Runnable {

    private DisplayPage displayPage;
    private Queue<GlobalSettings> queue;
    private Queue<GlobalSettings> scheduledQueue;
    private Thread t;
    private boolean running;
    private long lastCheck;
    private long checkTime;

    RunQueue(){
        this.displayPage = null;
        queue = new LinkedList<GlobalSettings>();
        scheduledQueue = new LinkedList<GlobalSettings>();
        running = false;
        checkTime = 60000;
        checkTime = 10;
        t = new Thread(this);
    }

    public void setDisplayPage(DisplayPage displayPage) {
        this.displayPage = displayPage;
    }

    void add(GlobalSettings settings, boolean scheduled){
        System.out.println("ADD");
        System.out.println(t.getState());
        if (scheduled){
            scheduledQueue.add(settings);
        }
        else {
            queue.add(settings);
        }
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
                EmailGetter getter = new EmailGetter(settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(), imageSaver);
                DisplayElem email = getter.fetch(settings.getKeywords());
                displayPage.addElement(email);
                settings.setLastRuntime(System.currentTimeMillis());
            }
            else{
                System.out.println("NULL");
            }
            if(scheduledQueue.peek() != null){
                GlobalSettings settings = scheduledQueue.peek();
                if (settings.getLastRuntime() + settings.getSchedule() <= System.currentTimeMillis()) {
                    scheduledQueue.remove();
                    queue.add(settings);
                }
            }
        }
    }
}
