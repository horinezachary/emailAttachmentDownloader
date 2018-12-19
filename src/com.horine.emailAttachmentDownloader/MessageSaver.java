package com.horine.emailAttachmentDownloader;

import java.util.ArrayList;

public class MessageSaver {

    String filepath;

    public MessageSaver(String filepath){
        this.filepath = filepath;
    }

    public void saveElements(ArrayList<DisplayElem> elements){
        //TODO message file saving
    }

    public ArrayList<DisplayElem> loadElements(){
        ArrayList<DisplayElem> elements = new ArrayList<DisplayElem>();
        //TODO message file loading

        return elements;
    }
}
